using System;
using System.Collections.Generic;
using Game.Areas;
using Game.Common;
using Game.Prefabs;
using Unity.Entities;

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
