using System;
using System.Collections.Generic;
using Colossal.Mathematics;
using Game.Areas;
using Game.Common;
using Game.Prefabs;
using Game.Simulation;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Transform = Game.Objects.Transform;

namespace CS2MCP
{
    public sealed partial class RequestHandlers
    {
        private BridgeResponse GetOperationalArea(BridgeRequest request)
        {
            if (!TryGetCity(out _, out BridgeResponse error))
            {
                return error;
            }
            if (!request.TryGetInt("index", out int index)
                || !request.TryGetInt("version", out int version))
            {
                return BridgeResponse.Error(400,
                    "provide ?index=&version= of a building from /city/buildings");
            }

            var building = new Entity { Index = index, Version = version };
            if (!EntityManager.Exists(building)
                || !EntityManager.HasComponent<Game.Buildings.Building>(building))
            {
                return BridgeResponse.Error(404,
                    $"entity {index}:{version} is not an existing building");
            }

            PrefabSystem prefabSystem = World.GetOrCreateSystemManaged<PrefabSystem>();
            string buildingName = GetEntityPrefabName(prefabSystem, building);
            var areas = new List<object>();
            int editableAreaCount = 0;
            if (EntityManager.HasBuffer<Game.Areas.SubArea>(building))
            {
                DynamicBuffer<Game.Areas.SubArea> subAreas =
                    EntityManager.GetBuffer<Game.Areas.SubArea>(building, isReadOnly: true);
                foreach (Game.Areas.SubArea subArea in subAreas)
                {
                    if (subArea.m_Area == Entity.Null || !EntityManager.Exists(subArea.m_Area))
                    {
                        continue;
                    }
                    object areaView = BuildOperationalAreaView(
                        prefabSystem,
                        building,
                        subArea.m_Area,
                        out bool editable);
                    areas.Add(areaView);
                    if (editable)
                    {
                        editableAreaCount++;
                    }
                }
            }

            return BridgeResponse.Json(new
            {
                building = buildingName,
                entity = new { index, version },
                areaCount = areas.Count,
                editableAreaCount,
                areas,
                note = areas.Count == 0
                    ? "this building has no owned operational area"
                    : "read-only snapshot; storage capacity is calculated with the game's AreaUtils and extractor fields are current simulation state",
            });
        }

        private BridgeResponse ExpandOperationalArea(BridgeRequest request)
        {
            if (!TryGetCity(out _, out BridgeResponse error))
            {
                return error;
            }
            if (!request.TryGetInt("index", out int index)
                || !request.TryGetInt("version", out int version))
            {
                return BridgeResponse.Error(400,
                    "provide ?index=&version= of a landfill building from /city/buildings");
            }
            if (!request.TryGetFloat("target_area_m2", out float targetArea)
                || targetArea < 64f
                || targetArea > 250000f)
            {
                return BridgeResponse.Error(400,
                    "target_area_m2 must be between 64 and 250000 square metres");
            }

            var building = new Entity { Index = index, Version = version };
            if (!EntityManager.Exists(building)
                || !EntityManager.HasComponent<Game.Buildings.Building>(building))
            {
                return BridgeResponse.Error(404,
                    $"entity {index}:{version} is not an existing building");
            }
            if (!TryResolveExpandableOperationalArea(
                    building,
                    out Entity area,
                    out Entity owner,
                    out Entity prefabEntity,
                    out DynamicBuffer<Node> currentNodes,
                    out bool isStorage,
                    out ExtractorAreaData? extractorData,
                    out BridgeResponse areaError))
            {
                return areaError;
            }

            float2 lockedStart = currentNodes[0].m_Position.xz;
            float2 lockedEnd = currentNodes[1].m_Position.xz;
            float2 lockedEdge = lockedEnd - lockedStart;
            float lockedLength = math.length(lockedEdge);
            if (lockedLength < 8f)
            {
                return BridgeResponse.Error(409,
                    "the building-side locked edge is too short to expand safely");
            }

            float2 tangent = lockedEdge / lockedLength;
            float2 normal = new float2(-tangent.y, tangent.x);
            float2 lockedMid = (lockedStart + lockedEnd) * 0.5f;
            float2 areaCenter = float2.zero;
            for (int i = 2; i < currentNodes.Length; i++)
            {
                areaCenter += currentNodes[i].m_Position.xz;
            }
            areaCenter /= currentNodes.Length - 2;
            float signedDepth = math.dot(areaCenter - lockedMid, normal);
            if (math.abs(signedDepth) < 8f)
            {
                return BridgeResponse.Error(409,
                    "the operational area does not have a usable free side away from the building");
            }
            if (signedDepth < 0f)
            {
                normal = -normal;
            }

            float previousPolygonArea = CalculatePolygonArea(currentNodes);
            if (targetArea <= previousPolygonArea + 1f)
            {
                return BridgeResponse.Error(409,
                    $"target_area_m2 must exceed the current {previousPolygonArea:F1} m2 area; shrinking is not supported");
            }

            TerrainSystem terrain = World.GetOrCreateSystemManaged<TerrainSystem>();
            TerrainHeightData heightData = terrain.GetHeightData();
            Node[] expandedNodeArray = null;
            OperationalResourceScore selectedResourceScore = default;
            int candidatesTested = 0;
            string lastRejection = null;
            float[] skews = { 0f, -15f, 15f, -30f, 30f };
            foreach (float skewDegrees in skews)
            {
                candidatesTested++;
                if (!TryPlanOperationalAreaFan(
                        currentNodes,
                        lockedStart,
                        lockedEnd,
                        tangent,
                        normal,
                        targetArea,
                        skewDegrees,
                        ref heightData,
                        out Node[] candidateNodes,
                        out _,
                        out string planError))
                {
                    lastRejection = planError;
                    continue;
                }
                if (!IsOperationalAreaCandidateClear(
                        building,
                        candidateNodes,
                        out string obstacleReason))
                {
                    lastRejection = obstacleReason;
                    continue;
                }
                if (extractorData.HasValue)
                {
                    if (!TryScoreOperationalAreaResource(
                            candidateNodes,
                            extractorData.Value,
                            out OperationalResourceScore resourceScore,
                            out string resourceError))
                    {
                        lastRejection = resourceError;
                        continue;
                    }
                    if (expandedNodeArray == null
                        || resourceScore.RemainingAmount > selectedResourceScore.RemainingAmount
                        || (math.abs(resourceScore.RemainingAmount - selectedResourceScore.RemainingAmount) < 0.1f
                            && resourceScore.Coverage > selectedResourceScore.Coverage))
                    {
                        expandedNodeArray = candidateNodes;
                        selectedResourceScore = resourceScore;
                    }
                    continue;
                }
                expandedNodeArray = candidateNodes;
                break;
            }
            if (expandedNodeArray == null)
            {
                return BridgeResponse.Error(409,
                    $"no clear expansion fan could reach {targetArea:F1} m2 after {candidatesTested} candidates: {lastRejection ?? "no valid geometry"}");
            }

            Geometry geometry = EntityManager.GetComponentData<Geometry>(area);
            int previousCapacity = isStorage
                ? AreaUtils.CalculateStorageCapacity(
                    geometry,
                    EntityManager.GetComponentData<StorageAreaData>(prefabEntity))
                : -1;
            PrefabSystem prefabSystem = World.GetOrCreateSystemManaged<PrefabSystem>();
            PrefabBase prefab = prefabSystem.GetPrefab<PrefabBase>(prefabEntity);
            BridgeToolSystem tool = World.GetOrCreateSystemManaged<BridgeToolSystem>();
            if (!tool.TryQueueOperationalAreaExpansion(
                    area,
                    owner,
                    prefabEntity,
                    prefab,
                    expandedNodeArray,
                    geometry.m_SurfaceArea,
                    previousCapacity,
                    targetArea,
                    isStorage ? "storage" : "extractor",
                    extractorData.HasValue ? extractorData.Value.m_MapFeature.ToString() : null,
                    selectedResourceScore.RemainingAmount,
                    selectedResourceScore.Coverage,
                    request))
            {
                return BridgeResponse.Error(409,
                    "another build operation is in progress, retry shortly");
            }
            return null;
        }

        private bool TryResolveExpandableOperationalArea(
            Entity building,
            out Entity area,
            out Entity owner,
            out Entity prefabEntity,
            out DynamicBuffer<Node> nodes,
            out bool isStorage,
            out ExtractorAreaData? extractorData,
            out BridgeResponse error)
        {
            area = Entity.Null;
            owner = Entity.Null;
            prefabEntity = Entity.Null;
            nodes = default;
            isStorage = false;
            extractorData = null;
            error = null;
            if (!EntityManager.HasBuffer<Game.Areas.SubArea>(building))
            {
                error = BridgeResponse.Error(409,
                    "building has no owned operational areas");
                return false;
            }

            DynamicBuffer<Game.Areas.SubArea> subAreas =
                EntityManager.GetBuffer<Game.Areas.SubArea>(building, isReadOnly: true);
            foreach (Game.Areas.SubArea subArea in subAreas)
            {
                Entity candidate = subArea.m_Area;
                if (candidate == Entity.Null
                    || !EntityManager.Exists(candidate)
                    || EntityManager.HasComponent<Deleted>(candidate)
                    || !EntityManager.HasComponent<Lot>(candidate)
                    || !EntityManager.HasComponent<Geometry>(candidate)
                    || !EntityManager.HasComponent<PrefabRef>(candidate)
                    || !EntityManager.HasComponent<Owner>(candidate)
                    || !EntityManager.HasBuffer<Node>(candidate)
                    || !IsAreaOwnedBy(candidate, building))
                {
                    continue;
                }

                Entity candidatePrefab =
                    EntityManager.GetComponentData<PrefabRef>(candidate).m_Prefab;
                DynamicBuffer<Node> candidateNodes =
                    EntityManager.GetBuffer<Node>(candidate, isReadOnly: true);
                bool candidateStorage = EntityManager.HasComponent<Storage>(candidate)
                    && EntityManager.HasComponent<StorageAreaData>(candidatePrefab)
                    && (EntityManager.GetComponentData<StorageAreaData>(candidatePrefab).m_Resources
                        & Game.Economy.Resource.Garbage) != 0;
                bool candidateExtractor = EntityManager.HasComponent<Extractor>(candidate)
                    && EntityManager.HasComponent<ExtractorAreaData>(candidatePrefab);
                if (candidateNodes.Length < 4
                    || candidateNodes.Length > 16
                    || (!candidateStorage && !candidateExtractor))
                {
                    continue;
                }
                if (area != Entity.Null)
                {
                    error = BridgeResponse.Error(409,
                        "building has multiple expandable storage areas; v0 requires exactly one");
                    return false;
                }
                area = candidate;
                owner = EntityManager.GetComponentData<Owner>(candidate).m_Owner;
                prefabEntity = candidatePrefab;
                nodes = candidateNodes;
                isStorage = candidateStorage;
                extractorData = candidateExtractor
                    ? EntityManager.GetComponentData<ExtractorAreaData>(candidatePrefab)
                    : (ExtractorAreaData?)null;
            }

            if (area == Entity.Null)
            {
                error = BridgeResponse.Error(409,
                    "no supported owner-linked landfill storage or extractor area is available");
                return false;
            }
            return true;
        }

        private static float CalculatePolygonArea(DynamicBuffer<Node> nodes)
        {
            float area = 0f;
            for (int i = 0; i < nodes.Length; i++)
            {
                float2 current = nodes[i].m_Position.xz;
                float2 next = nodes[(i + 1) % nodes.Length].m_Position.xz;
                area += current.x * next.y - next.x * current.y;
            }
            return math.abs(area) * 0.5f;
        }

        private static float CalculatePolygonArea(Node[] nodes)
        {
            float area = 0f;
            for (int i = 0; i < nodes.Length; i++)
            {
                float2 current = nodes[i].m_Position.xz;
                float2 next = nodes[(i + 1) % nodes.Length].m_Position.xz;
                area += current.x * next.y - next.x * current.y;
            }
            return math.abs(area) * 0.5f;
        }

        private static float CalculatePolygonArea(List<float2> nodes)
        {
            float area = 0f;
            for (int i = 0; i < nodes.Count; i++)
            {
                float2 current = nodes[i];
                float2 next = nodes[(i + 1) % nodes.Count];
                area += current.x * next.y - next.x * current.y;
            }
            return math.abs(area) * 0.5f;
        }

        private bool TryPlanOperationalAreaFan(
            DynamicBuffer<Node> nodes,
            float2 lockedStart,
            float2 lockedEnd,
            float2 tangent,
            float2 normal,
            float targetArea,
            float skewDegrees,
            ref TerrainHeightData heightData,
            out Node[] result,
            out float resultArea,
            out string error)
        {
            result = null;
            resultArea = 0f;
            error = null;
            const int arcSegments = 6;
            float halfAngle = math.radians(55f);
            float skew = math.radians(skewDegrees);
            float2 center = (lockedStart + lockedEnd) * 0.5f;
            var existing = new List<float2>(nodes.Length);
            for (int i = 0; i < nodes.Length; i++)
            {
                existing.Add(nodes[i].m_Position.xz);
            }

            List<float2> bestHull = null;
            float lowRadius = 0f;
            float highRadius = math.max(8f, math.distance(lockedStart, lockedEnd) * 0.5f);
            while (highRadius <= 512f)
            {
                bestHull = BuildOperationalAreaFanHull(
                    existing, center, tangent, normal, highRadius, skew, halfAngle, arcSegments);
                if (bestHull != null && CalculatePolygonArea(bestHull) >= targetArea)
                {
                    break;
                }
                highRadius *= 1.5f;
            }
            if (bestHull == null || CalculatePolygonArea(bestHull) < targetArea)
            {
                error = "target area requires a fan radius beyond the 512 m planning limit";
                return false;
            }
            for (int iteration = 0; iteration < 18; iteration++)
            {
                float radius = (lowRadius + highRadius) * 0.5f;
                List<float2> hull = BuildOperationalAreaFanHull(
                    existing, center, tangent, normal, radius, skew, halfAngle, arcSegments);
                if (hull != null && CalculatePolygonArea(hull) >= targetArea)
                {
                    highRadius = radius;
                    bestHull = hull;
                }
                else
                {
                    lowRadius = radius;
                }
            }

            if (!TryOrientOperationalAreaHull(bestHull, lockedStart, lockedEnd, out List<float2> oriented))
            {
                error = "planned fan did not preserve the locked building edge";
                return false;
            }
            if (oriented.Count > 16)
            {
                error = $"planned fan needs {oriented.Count} nodes, above the 16-node safety limit";
                return false;
            }
            result = new Node[oriented.Count];
            for (int i = 0; i < oriented.Count; i++)
            {
                Node template = FindNearestOperationalAreaNode(nodes, oriented[i]);
                result[i] = CreateOperationalAreaNode(template, oriented[i], ref heightData);
            }
            resultArea = CalculatePolygonArea(result);
            if (!HasMinimumOperationalAreaSpacing(result, 4f))
            {
                error = "planned fan places adjacent nodes closer than the native 4 m limit";
                result = null;
                return false;
            }
            if (resultArea < targetArea - 1f)
            {
                error = "planned fan fell below the requested area after terrain projection";
                result = null;
                return false;
            }
            return true;
        }

        private static bool HasMinimumOperationalAreaSpacing(Node[] nodes, float minimumSpacing)
        {
            float minimumSquared = minimumSpacing * minimumSpacing;
            for (int i = 0; i < nodes.Length; i++)
            {
                float2 current = nodes[i].m_Position.xz;
                float2 next = nodes[(i + 1) % nodes.Length].m_Position.xz;
                if (math.distancesq(current, next) < minimumSquared)
                {
                    return false;
                }
            }
            return true;
        }

        private struct OperationalResourceScore
        {
            public float RemainingAmount;
            public float Coverage;
        }

        private bool TryScoreOperationalAreaResource(
            Node[] nodes,
            ExtractorAreaData extractorData,
            out OperationalResourceScore score,
            out string error)
        {
            score = default;
            error = null;
            MapFeature feature = extractorData.m_MapFeature;
            if (feature == MapFeature.Forest)
            {
                error = "forest extractors require the tree-entity scorer, which is not enabled yet";
                return false;
            }
            if (feature != MapFeature.FertileLand
                && feature != MapFeature.Ore
                && feature != MapFeature.Oil
                && feature != MapFeature.Fish)
            {
                return !extractorData.m_RequireNaturalResource;
            }

            NaturalResourceSystem resources =
                World.GetOrCreateSystemManaged<NaturalResourceSystem>();
            NativeArray<NaturalResourceCell> map = resources.GetMap(
                readOnly: true,
                out Unity.Jobs.JobHandle dependencies);
            dependencies.Complete();
            int textureSize = (int)math.round(math.sqrt(map.Length));
            if (textureSize <= 0 || textureSize * textureSize != map.Length)
            {
                error = "natural-resource map has an unexpected size";
                return false;
            }

            float cellSize = (float)CellMapSystem<NaturalResourceCell>.kMapSize / textureSize;
            float halfTexture = textureSize * 0.5f;
            float cellArea = cellSize * cellSize;
            float resourceBearingArea = 0f;
            float polygonArea = CalculatePolygonArea(nodes);
            float2 origin = nodes[0].m_Position.xz;
            for (int triangleIndex = 1; triangleIndex < nodes.Length - 1; triangleIndex++)
            {
                var triangle = new Triangle2(
                    origin,
                    nodes[triangleIndex].m_Position.xz,
                    nodes[triangleIndex + 1].m_Position.xz);
                float2 minimum = math.min(triangle.a, math.min(triangle.b, triangle.c));
                float2 maximum = math.max(triangle.a, math.max(triangle.b, triangle.c));
                int minX = math.clamp((int)math.floor(minimum.x / cellSize + halfTexture), 0, textureSize - 1);
                int maxX = math.clamp((int)math.floor(maximum.x / cellSize + halfTexture), 0, textureSize - 1);
                int minY = math.clamp((int)math.floor(minimum.y / cellSize + halfTexture), 0, textureSize - 1);
                int maxY = math.clamp((int)math.floor(maximum.y / cellSize + halfTexture), 0, textureSize - 1);
                for (int y = minY; y <= maxY; y++)
                {
                    for (int x = minX; x <= maxX; x++)
                    {
                        NaturalResourceAmount amount = GetOperationalResourceAmount(
                            map[x + y * textureSize],
                            feature);
                        float remaining = math.max(0f, amount.m_Base - amount.m_Used);
                        if (remaining <= 0f)
                        {
                            continue;
                        }
                        var cellBounds = new Bounds2
                        {
                            min = new float2((x - halfTexture) * cellSize, (y - halfTexture) * cellSize),
                            max = new float2((x + 1f - halfTexture) * cellSize, (y + 1f - halfTexture) * cellSize),
                        };
                        if (MathUtils.Intersect(cellBounds, triangle, out float intersectionArea))
                        {
                            score.RemainingAmount += remaining * intersectionArea / cellArea;
                            resourceBearingArea += intersectionArea;
                        }
                    }
                }
            }
            score.Coverage = polygonArea > 0.01f
                ? math.saturate(resourceBearingArea / polygonArea)
                : 0f;
            if (extractorData.m_RequireNaturalResource && score.RemainingAmount <= 0.01f)
            {
                error = $"planned {feature} extractor fan has no remaining resource coverage";
                return false;
            }
            return true;
        }

        private static NaturalResourceAmount GetOperationalResourceAmount(
            NaturalResourceCell cell,
            MapFeature feature)
        {
            switch (feature)
            {
                case MapFeature.FertileLand:
                    return cell.m_Fertility;
                case MapFeature.Ore:
                    return cell.m_Ore;
                case MapFeature.Oil:
                    return cell.m_Oil;
                case MapFeature.Fish:
                    return cell.m_Fish;
                default:
                    return default;
            }
        }

        private static List<float2> BuildOperationalAreaFanHull(
            List<float2> existing,
            float2 center,
            float2 tangent,
            float2 normal,
            float radius,
            float skew,
            float halfAngle,
            int arcSegments)
        {
            var points = new List<float2>(existing.Count + arcSegments + 1);
            points.AddRange(existing);
            for (int i = 0; i <= arcSegments; i++)
            {
                float angle = skew + halfAngle - 2f * halfAngle * i / arcSegments;
                float2 direction = normal * math.cos(angle) + tangent * math.sin(angle);
                points.Add(center + direction * radius);
            }
            return ConvexHull(points);
        }

        private static List<float2> ConvexHull(List<float2> points)
        {
            points.Sort((a, b) => a.x != b.x ? a.x.CompareTo(b.x) : a.y.CompareTo(b.y));
            var unique = new List<float2>(points.Count);
            foreach (float2 point in points)
            {
                if (unique.Count == 0 || math.distancesq(unique[unique.Count - 1], point) > 0.01f)
                {
                    unique.Add(point);
                }
            }
            if (unique.Count < 3)
            {
                return null;
            }
            var hull = new List<float2>(unique.Count * 2);
            foreach (float2 point in unique)
            {
                while (hull.Count >= 2
                    && Cross(hull[hull.Count - 1] - hull[hull.Count - 2], point - hull[hull.Count - 1]) <= 0f)
                {
                    hull.RemoveAt(hull.Count - 1);
                }
                hull.Add(point);
            }
            int lowerCount = hull.Count;
            for (int i = unique.Count - 2; i >= 0; i--)
            {
                float2 point = unique[i];
                while (hull.Count > lowerCount
                    && Cross(hull[hull.Count - 1] - hull[hull.Count - 2], point - hull[hull.Count - 1]) <= 0f)
                {
                    hull.RemoveAt(hull.Count - 1);
                }
                hull.Add(point);
            }
            hull.RemoveAt(hull.Count - 1);
            return hull;
        }

        private static float Cross(float2 a, float2 b) => a.x * b.y - a.y * b.x;

        private static bool TryOrientOperationalAreaHull(
            List<float2> hull,
            float2 lockedStart,
            float2 lockedEnd,
            out List<float2> oriented)
        {
            oriented = null;
            if (hull == null)
            {
                return false;
            }
            int start = hull.FindIndex(point => math.distancesq(point, lockedStart) < 0.01f);
            int end = hull.FindIndex(point => math.distancesq(point, lockedEnd) < 0.01f);
            if (start < 0 || end < 0)
            {
                return false;
            }
            if ((start + 1) % hull.Count != end)
            {
                if ((end + 1) % hull.Count != start)
                {
                    return false;
                }
                hull.Reverse();
                start = hull.FindIndex(point => math.distancesq(point, lockedStart) < 0.01f);
            }
            oriented = new List<float2>(hull.Count);
            for (int i = 0; i < hull.Count; i++)
            {
                oriented.Add(hull[(start + i) % hull.Count]);
            }
            return oriented.Count >= 3 && math.distancesq(oriented[1], lockedEnd) < 0.01f;
        }

        private bool IsOperationalAreaCandidateClear(
            Entity building,
            Node[] nodes,
            out string reason)
        {
            var polygon = new List<float2>(nodes.Length);
            foreach (Node node in nodes)
            {
                polygon.Add(node.m_Position.xz);
                if (!IsOnOwnedTile(node.m_Position))
                {
                    reason = "a planned fan node is outside owned map tiles";
                    return false;
                }
            }
            using (NativeArray<Entity> buildings = PlacedBuildingQuery.ToEntityArray(Allocator.Temp))
            {
                foreach (Entity other in buildings)
                {
                    if (other == building || IsAreaOwnedBy(other, building))
                    {
                        continue;
                    }
                    Transform transform = EntityManager.GetComponentData<Transform>(other);
                    PrefabRef prefabRef = EntityManager.GetComponentData<PrefabRef>(other);
                    float clearance = BuildingRadius(prefabRef.m_Prefab) + 2f;
                    if (clearance > 2f
                        && DistanceToPolygon(transform.m_Position.xz, polygon) < clearance)
                    {
                        reason = "planned fan intersects an existing building";
                        return false;
                    }
                }
            }
            using (NativeArray<Entity> roads = PlacedRoadQuery.ToEntityArray(Allocator.Temp))
            {
                foreach (Entity road in roads)
                {
                    PrefabRef prefabRef = EntityManager.GetComponentData<PrefabRef>(road);
                    float clearance = EntityManager.HasComponent<NetGeometryData>(prefabRef.m_Prefab)
                        ? EntityManager.GetComponentData<NetGeometryData>(prefabRef.m_Prefab).m_DefaultWidth * 0.5f + 2f
                        : 2f;
                    Game.Net.Curve curve = EntityManager.GetComponentData<Game.Net.Curve>(road);
                    for (int i = 0; i <= 24; i++)
                    {
                        float2 point = BezierPoint(curve.m_Bezier, i / 24f).xz;
                        if (DistanceToPolygon(point, polygon) < clearance)
                        {
                            reason = "planned fan intersects an existing road";
                            return false;
                        }
                    }
                }
            }
            reason = null;
            return true;
        }

        private static float DistanceToPolygon(float2 point, List<float2> polygon)
        {
            bool inside = false;
            float distance = float.MaxValue;
            for (int i = 0, j = polygon.Count - 1; i < polygon.Count; j = i++)
            {
                float2 a = polygon[j];
                float2 b = polygon[i];
                float2 edge = b - a;
                float t = math.saturate(math.dot(point - a, edge) / math.max(0.001f, math.lengthsq(edge)));
                distance = math.min(distance, math.distance(point, a + edge * t));
                if ((a.y > point.y) != (b.y > point.y)
                    && point.x < (b.x - a.x) * (point.y - a.y) / (b.y - a.y) + a.x)
                {
                    inside = !inside;
                }
            }
            return inside ? 0f : distance;
        }

        private static Node FindNearestOperationalAreaNode(DynamicBuffer<Node> nodes, float2 point)
        {
            int selected = 0;
            float best = float.MaxValue;
            for (int i = 0; i < nodes.Length; i++)
            {
                float distance = math.distancesq(nodes[i].m_Position.xz, point);
                if (distance < best)
                {
                    best = distance;
                    selected = i;
                }
            }
            return nodes[selected];
        }

        private static Node CreateOperationalAreaNode(
            Node template,
            float2 xz,
            ref TerrainHeightData heightData)
        {
            float3 position = new float3(xz.x, template.m_Position.y, xz.y);
            position.y = TerrainUtils.SampleHeight(ref heightData, position);
            template.m_Position = position;
            return template;
        }

        private object BuildOperationalAreaView(
            PrefabSystem prefabSystem,
            Entity building,
            Entity area,
            out bool editable)
        {
            Entity prefabEntity = EntityManager.HasComponent<PrefabRef>(area)
                ? EntityManager.GetComponentData<PrefabRef>(area).m_Prefab
                : Entity.Null;
            string prefabName = prefabEntity != Entity.Null
                ? GetEntityPrefabName(prefabSystem, area)
                : null;
            bool isStorage = EntityManager.HasComponent<Storage>(area);
            bool isExtractor = EntityManager.HasComponent<Extractor>(area);
            string kind = isStorage ? "storage" : isExtractor ? "extractor" : "other";

            Geometry? geometry = EntityManager.HasComponent<Geometry>(area)
                ? EntityManager.GetComponentData<Geometry>(area)
                : (Geometry?)null;
            var nodes = new List<object>();
            if (EntityManager.HasBuffer<Node>(area))
            {
                DynamicBuffer<Node> areaNodes = EntityManager.GetBuffer<Node>(area, isReadOnly: true);
                foreach (Node node in areaNodes)
                {
                    nodes.Add(new
                    {
                        x = node.m_Position.x,
                        y = node.m_Position.y,
                        z = node.m_Position.z,
                    });
                }
            }

            object storage = null;
            if (isStorage)
            {
                Storage value = EntityManager.GetComponentData<Storage>(area);
                int? capacity = null;
                if (geometry.HasValue
                    && prefabEntity != Entity.Null
                    && EntityManager.HasComponent<StorageAreaData>(prefabEntity))
                {
                    capacity = AreaUtils.CalculateStorageCapacity(
                        geometry.Value,
                        EntityManager.GetComponentData<StorageAreaData>(prefabEntity));
                }
                storage = new
                {
                    amount = value.m_Amount,
                    workAmount = value.m_WorkAmount,
                    capacity,
                };
            }

            object extraction = null;
            if (isExtractor)
            {
                Extractor value = EntityManager.GetComponentData<Extractor>(area);
                ExtractorAreaData? data = prefabEntity != Entity.Null
                    && EntityManager.HasComponent<ExtractorAreaData>(prefabEntity)
                    ? EntityManager.GetComponentData<ExtractorAreaData>(prefabEntity)
                    : (ExtractorAreaData?)null;
                extraction = new
                {
                    resource = data.HasValue ? data.Value.m_MapFeature.ToString() : null,
                    requiresNaturalResource = data.HasValue
                        ? (bool?)data.Value.m_RequireNaturalResource
                        : null,
                    resourceAmount = value.m_ResourceAmount,
                    maxConcentration = value.m_MaxConcentration,
                    extractedAmount = value.m_ExtractedAmount,
                    totalExtracted = value.m_TotalExtracted,
                    workAmount = value.m_WorkAmount,
                };
            }

            editable = IsAreaOwnedBy(area, building)
                && (isStorage || isExtractor)
                && nodes.Count >= 3;
            return new
            {
                kind,
                prefab = prefabName,
                editable,
                lockedBuildingEdge = EntityManager.HasComponent<Game.Areas.Lot>(area),
                surfaceArea = geometry.HasValue ? (float?)Math.Round(geometry.Value.m_SurfaceArea, 1) : null,
                nodes,
                storage,
                extraction,
            };
        }

        private bool IsAreaOwnedBy(Entity area, Entity expectedOwner)
        {
            Entity current = area;
            for (int depth = 0; depth < 8 && EntityManager.HasComponent<Owner>(current); depth++)
            {
                current = EntityManager.GetComponentData<Owner>(current).m_Owner;
                if (current == expectedOwner)
                {
                    return true;
                }
                if (current == Entity.Null || !EntityManager.Exists(current))
                {
                    break;
                }
            }
            return false;
        }

        private string GetEntityPrefabName(PrefabSystem prefabSystem, Entity entity)
        {
            if (!EntityManager.HasComponent<PrefabRef>(entity))
            {
                return null;
            }
            Entity prefabEntity = EntityManager.GetComponentData<PrefabRef>(entity).m_Prefab;
            PrefabBase prefab = prefabSystem.GetPrefab<PrefabBase>(prefabEntity);
            return prefab != null ? prefab.name : null;
        }
    }
}
