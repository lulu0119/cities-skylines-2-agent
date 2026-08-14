using System;
using System.Collections.Generic;
using Colossal.Collections;
using Colossal.Mathematics;
using Game.Areas;
using Game.Common;
using Game.Objects;
using Game.Prefabs;
using Game.Simulation;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
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
                return BridgeResponse.Error(BridgeErrorKind.InvalidArguments,
                    "provide ?index=&version= of a building from /city/buildings");
            }

            if (!TryResolveExistingEntity(index, version, out Entity building)
                || !EntityManager.HasComponent<Game.Buildings.Building>(building))
            {
                return BridgeResponse.Error(BridgeErrorKind.NotFound,
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
                return BridgeResponse.Error(BridgeErrorKind.InvalidArguments,
                    "provide ?index=&version= of a landfill building from /city/buildings");
            }
            if (!request.TryGetFloat("target_area_m2", out float targetArea)
                || targetArea < 64f
                || targetArea > 250000f)
            {
                return BridgeResponse.Error(BridgeErrorKind.InvalidArguments,
                    "target_area_m2 must be between 64 and 250000 square metres");
            }

            if (!TryResolveExistingEntity(index, version, out Entity building)
                || !EntityManager.HasComponent<Game.Buildings.Building>(building))
            {
                return BridgeResponse.Error(BridgeErrorKind.NotFound,
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
                return BridgeResponse.Error(BridgeErrorKind.Conflict,
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
                return BridgeResponse.Error(BridgeErrorKind.Conflict,
                    "the operational area does not have a usable free side away from the building");
            }
            if (signedDepth < 0f)
            {
                normal = -normal;
            }

            float previousPolygonArea = CalculatePolygonArea(currentNodes);
            if (targetArea <= previousPolygonArea + 1f)
            {
                return BridgeResponse.Error(BridgeErrorKind.Conflict,
                    $"target_area_m2 must exceed the current {previousPolygonArea:F1} m2 area; shrinking is not supported");
            }

            TerrainSystem terrain = World.GetOrCreateSystemManaged<TerrainSystem>();
            TerrainHeightData heightData = terrain.GetHeightData();
            List<OperationalAreaObstacle> obstacles = CollectOperationalAreaObstacles(building);
            Node[] expandedNodeArray = null;
            OperationalResourceScore selectedResourceScore = default;
            int candidatesTested = 0;
            string lastRejection = null;
            foreach (float tangentShift in OperationalAreaPlanningMath.CenterShifts(lockedLength))
            {
                candidatesTested++;
                if (!TryPlanOperationalAreaExpansion(
                        currentNodes,
                        lockedStart,
                        lockedEnd,
                        tangent,
                        normal,
                        targetArea,
                        tangentShift,
                        obstacles,
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
                return BridgeResponse.Error(BridgeErrorKind.Conflict,
                    $"no clear expansion could reach {targetArea:F1} m2 after {candidatesTested} candidates: {lastRejection ?? "no valid geometry"}");
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
                    selectedResourceScore.SampleCount,
                    selectedResourceScore.MaxConcentration,
                    request))
            {
                return BridgeResponse.Error(BridgeErrorKind.Conflict,
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
                error = BridgeResponse.Error(BridgeErrorKind.Conflict,
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
                    error = BridgeResponse.Error(BridgeErrorKind.Conflict,
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
                error = BridgeResponse.Error(BridgeErrorKind.Conflict,
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

        private bool TryPlanOperationalAreaExpansion(
            DynamicBuffer<Node> nodes,
            float2 lockedStart,
            float2 lockedEnd,
            float2 tangent,
            float2 normal,
            float targetArea,
            float tangentShift,
            IReadOnlyList<OperationalAreaObstacle> obstacles,
            ref TerrainHeightData heightData,
            out Node[] result,
            out float resultArea,
            out string error)
        {
            result = null;
            resultArea = 0f;
            error = null;
            var existing = new List<float2>(nodes.Length);
            for (int i = 0; i < nodes.Length; i++)
            {
                existing.Add(nodes[i].m_Position.xz);
            }

            if (!OperationalAreaPlanningMath.TryPlanExpansion(
                    existing,
                    lockedStart,
                    lockedEnd,
                    tangent,
                    normal,
                    targetArea,
                    tangentShift,
                    obstacles,
                    out List<float2> oriented,
                    out _,
                    out error))
            {
                return false;
            }

            result = new Node[oriented.Count];
            for (int i = 0; i < oriented.Count; i++)
            {
                Node template = FindNearestOperationalAreaNode(nodes, oriented[i]);
                result[i] = CreateOperationalAreaNode(template, oriented[i], ref heightData);
            }
            resultArea = CalculatePolygonArea(result);
            if (!HasMinimumOperationalAreaSpacing(
                    result,
                    OperationalAreaPlanningMath.MinNodeDistance))
            {
                error = "planned expansion places adjacent nodes closer than the native 4 m limit";
                result = null;
                return false;
            }
            if (resultArea < targetArea - 1f)
            {
                error = "planned expansion fell below the requested area after terrain projection";
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
            public int SampleCount;
            public float MaxConcentration;
        }

        private struct ForestResourceIterator
            : INativeQuadTreeIterator<Entity, QuadTreeBoundsXZ>
        {
            public Bounds2 Bounds;
            public NativeArray<float2> Polygon;
            public ComponentLookup<Tree> Trees;
            public ComponentLookup<Plant> Plants;
            public ComponentLookup<Transform> Transforms;
            public ComponentLookup<Damaged> Damaged;
            public ComponentLookup<PrefabRef> PrefabRefs;
            public ComponentLookup<TreeData> TreePrefabs;
            public ComponentLookup<Decoration> Decorations;
            public float RemainingAmount;
            public float MaxConcentration;
            public int TreeCount;

            public bool Intersect(QuadTreeBoundsXZ bounds)
            {
                const BoundsMask required = BoundsMask.IsTree | BoundsMask.NotOverridden;
                return (bounds.m_Mask & required) == required
                    && MathUtils.Intersect(bounds.m_Bounds.xz, Bounds);
            }

            public void Iterate(QuadTreeBoundsXZ bounds, Entity entity)
            {
                const BoundsMask required = BoundsMask.IsTree | BoundsMask.NotOverridden;
                if ((bounds.m_Mask & required) != required
                    || !MathUtils.Intersect(bounds.m_Bounds.xz, Bounds)
                    || (Decorations.HasComponent(entity) && Decorations.IsComponentEnabled(entity))
                    || !Trees.HasComponent(entity)
                    || !Plants.HasComponent(entity)
                    || !Transforms.HasComponent(entity)
                    || !PrefabRefs.HasComponent(entity))
                {
                    return;
                }

                float2 position = Transforms[entity].m_Position.xz;
                if (!ContainsPoint(position))
                {
                    return;
                }

                Entity prefab = PrefabRefs[entity].m_Prefab;
                if (!TreePrefabs.TryGetComponent(prefab, out TreeData treeData)
                    || treeData.m_WoodAmount < Game.Objects.ObjectUtils.MIN_TREE_WOOD_RESOURCE)
                {
                    return;
                }

                Damaged.TryGetComponent(entity, out Damaged damaged);
                float amount = Game.Objects.ObjectUtils.CalculateWoodAmount(
                    Trees[entity],
                    Plants[entity],
                    damaged,
                    treeData);
                if (amount <= 0f)
                {
                    return;
                }

                RemainingAmount += amount;
                MaxConcentration = math.max(MaxConcentration, amount / treeData.m_WoodAmount);
                TreeCount++;
            }

            private bool ContainsPoint(float2 point)
            {
                bool inside = false;
                int previous = Polygon.Length - 1;
                for (int current = 0; current < Polygon.Length; current++)
                {
                    float2 a = Polygon[current];
                    float2 b = Polygon[previous];
                    bool crosses = (a.y > point.y) != (b.y > point.y)
                        && point.x < (b.x - a.x) * (point.y - a.y)
                            / (b.y - a.y) + a.x;
                    if (crosses)
                    {
                        inside = !inside;
                    }
                    previous = current;
                }
                return inside;
            }
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
                return TryScoreForestOperationalArea(
                    nodes,
                    extractorData.m_RequireNaturalResource,
                    out score,
                    out error);
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
                error = $"planned {feature} extractor expansion has no remaining resource coverage";
                return false;
            }
            return true;
        }

        private bool TryScoreForestOperationalArea(
            Node[] nodes,
            bool requireNaturalResource,
            out OperationalResourceScore score,
            out string error)
        {
            score = default;
            error = null;
            var polygon = new NativeArray<float2>(nodes.Length, Allocator.Temp);
            try
            {
                float2 minimum = new float2(float.MaxValue);
                float2 maximum = new float2(float.MinValue);
                for (int i = 0; i < nodes.Length; i++)
                {
                    float2 position = nodes[i].m_Position.xz;
                    polygon[i] = position;
                    minimum = math.min(minimum, position);
                    maximum = math.max(maximum, position);
                }

                Game.Objects.SearchSystem search =
                    World.GetOrCreateSystemManaged<Game.Objects.SearchSystem>();
                NativeQuadTree<Entity, QuadTreeBoundsXZ> searchTree =
                    search.GetStaticSearchTree(readOnly: true, out JobHandle dependencies);
                dependencies.Complete();
                var iterator = new ForestResourceIterator
                {
                    Bounds = new Bounds2 { min = minimum, max = maximum },
                    Polygon = polygon,
                    Trees = m_System.GetComponentLookup<Tree>(true),
                    Plants = m_System.GetComponentLookup<Plant>(true),
                    Transforms = m_System.GetComponentLookup<Transform>(true),
                    Damaged = m_System.GetComponentLookup<Damaged>(true),
                    PrefabRefs = m_System.GetComponentLookup<PrefabRef>(true),
                    TreePrefabs = m_System.GetComponentLookup<TreeData>(true),
                    Decorations = m_System.GetComponentLookup<Decoration>(true),
                };
                searchTree.Iterate(ref iterator);
                score.RemainingAmount = iterator.RemainingAmount;
                score.MaxConcentration = math.saturate(iterator.MaxConcentration);
                score.Coverage = score.MaxConcentration;
                score.SampleCount = iterator.TreeCount;
            }
            finally
            {
                polygon.Dispose();
            }

            if (requireNaturalResource && score.RemainingAmount <= 0.01f)
            {
                error = "planned Forest extractor expansion contains no productive tree resources";
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

        private List<OperationalAreaObstacle> CollectOperationalAreaObstacles(Entity building)
        {
            var obstacles = new List<OperationalAreaObstacle>();
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
                    if (clearance > 2f)
                    {
                        obstacles.Add(new OperationalAreaObstacle(transform.m_Position.xz, clearance));
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
                        obstacles.Add(new OperationalAreaObstacle(
                            BezierPoint(curve.m_Bezier, i / 24f).xz,
                            clearance));
                    }
                }
            }
            return obstacles;
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
                    reason = "a planned expansion node is outside owned map tiles";
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
                        && OperationalAreaPlanningMath.DistanceToPolygon(transform.m_Position.xz, polygon) < clearance)
                    {
                        reason = "planned expansion intersects an existing building";
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
                        if (OperationalAreaPlanningMath.DistanceToPolygon(point, polygon) < clearance)
                        {
                            reason = "planned expansion intersects an existing road";
                            return false;
                        }
                    }
                }
            }
            reason = null;
            return true;
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
