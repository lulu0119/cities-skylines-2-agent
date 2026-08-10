using System;
using System.Collections.Generic;
using Game.Areas;
using Game.Common;
using Game.Prefabs;
using Game.Simulation;
using Unity.Entities;
using Unity.Mathematics;

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
            if (!request.TryGetFloat("extra_depth_m", out float extraDepth)
                || extraDepth < 8f
                || extraDepth > 64f)
            {
                return BridgeResponse.Error(400,
                    "extra_depth_m must be between 8 and 64 metres");
            }

            var building = new Entity { Index = index, Version = version };
            if (!EntityManager.Exists(building)
                || !EntityManager.HasComponent<Game.Buildings.Building>(building))
            {
                return BridgeResponse.Error(404,
                    $"entity {index}:{version} is not an existing building");
            }
            if (!TryResolveExpandableStorageArea(
                    building,
                    out Entity area,
                    out Entity owner,
                    out Entity prefabEntity,
                    out DynamicBuffer<Node> currentNodes,
                    out BridgeResponse areaError))
            {
                return areaError;
            }

            var expandedNodes = new Node[currentNodes.Length];
            for (int i = 0; i < currentNodes.Length; i++)
            {
                expandedNodes[i] = currentNodes[i];
            }

            float2 lockedStart = expandedNodes[0].m_Position.xz;
            float2 lockedEnd = expandedNodes[1].m_Position.xz;
            float2 lockedEdge = lockedEnd - lockedStart;
            float lockedLength = math.length(lockedEdge);
            if (lockedLength < 8f)
            {
                return BridgeResponse.Error(409,
                    "the building-side locked edge is too short to expand safely");
            }

            float2 normal = new float2(-lockedEdge.y, lockedEdge.x) / lockedLength;
            float2 lockedMid = (lockedStart + lockedEnd) * 0.5f;
            float2 freeMid = (expandedNodes[2].m_Position.xz
                + expandedNodes[3].m_Position.xz) * 0.5f;
            float signedDepth = math.dot(freeMid - lockedMid, normal);
            if (math.abs(signedDepth) < 8f)
            {
                return BridgeResponse.Error(409,
                    "the operational area is not a supported four-corner extrusion");
            }
            if (signedDepth < 0f)
            {
                normal = -normal;
            }

            TerrainSystem terrain = World.GetOrCreateSystemManaged<TerrainSystem>();
            TerrainHeightData heightData = terrain.GetHeightData();
            for (int i = 2; i < 4; i++)
            {
                Node node = expandedNodes[i];
                float2 moved = node.m_Position.xz + normal * extraDepth;
                float3 position = new float3(moved.x, node.m_Position.y, moved.y);
                position.y = TerrainUtils.SampleHeight(ref heightData, position);
                node.m_Position = position;
                expandedNodes[i] = node;
            }

            float previousPolygonArea = CalculatePolygonArea(currentNodes);
            float expandedPolygonArea = CalculatePolygonArea(expandedNodes);
            if (expandedPolygonArea <= previousPolygonArea + 1f)
            {
                return BridgeResponse.Error(409,
                    "requested geometry did not increase the operational area");
            }

            Geometry geometry = EntityManager.GetComponentData<Geometry>(area);
            StorageAreaData storageData =
                EntityManager.GetComponentData<StorageAreaData>(prefabEntity);
            int previousCapacity = AreaUtils.CalculateStorageCapacity(geometry, storageData);
            PrefabSystem prefabSystem = World.GetOrCreateSystemManaged<PrefabSystem>();
            PrefabBase prefab = prefabSystem.GetPrefab<PrefabBase>(prefabEntity);
            BridgeToolSystem tool = World.GetOrCreateSystemManaged<BridgeToolSystem>();
            if (!tool.TryQueueOperationalAreaExpansion(
                    area,
                    owner,
                    prefabEntity,
                    prefab,
                    expandedNodes,
                    geometry.m_SurfaceArea,
                    previousCapacity,
                    extraDepth,
                    request))
            {
                return BridgeResponse.Error(409,
                    "another build operation is in progress, retry shortly");
            }
            return null;
        }

        private bool TryResolveExpandableStorageArea(
            Entity building,
            out Entity area,
            out Entity owner,
            out Entity prefabEntity,
            out DynamicBuffer<Node> nodes,
            out BridgeResponse error)
        {
            area = Entity.Null;
            owner = Entity.Null;
            prefabEntity = Entity.Null;
            nodes = default;
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
                    || !EntityManager.HasComponent<Storage>(candidate)
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
                if (candidateNodes.Length != 4
                    || !EntityManager.HasComponent<StorageAreaData>(candidatePrefab)
                    || (EntityManager.GetComponentData<StorageAreaData>(candidatePrefab).m_Resources
                        & Game.Economy.Resource.Garbage) == 0)
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
            }

            if (area == Entity.Null)
            {
                error = BridgeResponse.Error(409,
                    "no owner-linked four-corner landfill storage area is available; v0 does not edit extractor or irregular polygons");
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
