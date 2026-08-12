using System;
using System.Collections.Generic;
using Game.City;
using Game.Prefabs;
using Game.Simulation;
using Game.Zones;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace CS2MCP
{
    /// <summary>
    /// Zoning endpoints. Zone cells live in a Cell buffer on Block entities
    /// (blocks are auto-created along roads). Painting = rewriting Cell.m_Zone
    /// for matching cells and marking the block Updated so the game's zone and
    /// spawning systems react - same net effect as the zone tool's apply job.
    /// </summary>
    public sealed partial class RequestHandlers
    {
        private const float kCellSize = 8f;

        private EntityQuery m_ZonePrefabQuery;
        private bool m_ZonePrefabQueryCreated;
        private EntityQuery m_ZoneBlockQuery;
        private bool m_ZoneBlockQueryCreated;

        private EntityQuery ZonePrefabQuery
        {
            get
            {
                if (!m_ZonePrefabQueryCreated)
                {
                    m_ZonePrefabQuery = EntityManager.CreateEntityQuery(
                        ComponentType.ReadOnly<PrefabData>(),
                        ComponentType.ReadOnly<ZoneData>());
                    m_ZonePrefabQueryCreated = true;
                }
                return m_ZonePrefabQuery;
            }
        }

        private EntityQuery ZoneBlockQuery
        {
            get
            {
                if (!m_ZoneBlockQueryCreated)
                {
                    m_ZoneBlockQuery = EntityManager.CreateEntityQuery(new EntityQueryDesc
                    {
                        All = new[]
                        {
                            ComponentType.ReadOnly<Block>(),
                            ComponentType.ReadOnly<Cell>(),
                        },
                        None = new[]
                        {
                            ComponentType.ReadOnly<Game.Tools.Temp>(),
                            ComponentType.ReadOnly<Game.Common.Deleted>(),
                        },
                    });
                    m_ZoneBlockQueryCreated = true;
                }
                return m_ZoneBlockQuery;
            }
        }

        private BridgeResponse GetZoneTypes()
        {
            if (!TryGetCity(out _, out BridgeResponse error))
            {
                return error;
            }

            PrefabSystem prefabSystem = World.GetOrCreateSystemManaged<PrefabSystem>();
            var zones = new List<object>();
            using (NativeArray<Entity> entities = ZonePrefabQuery.ToEntityArray(Allocator.Temp))
            {
                foreach (Entity entity in entities)
                {
                    PrefabBase prefab = prefabSystem.GetPrefab<PrefabBase>(entity);
                    if (prefab == null)
                    {
                        continue;
                    }
                    ZoneData zoneData = EntityManager.GetComponentData<ZoneData>(entity);
                    zones.Add(new
                    {
                        name = prefab.name,
                        areaType = zoneData.m_AreaType.ToString(),
                        office = zoneData.IsOffice(),
                        locked = IsLocked(entity),
                    });
                }
            }

            return BridgeResponse.Json(new
            {
                note = "use 'name' with /build/zone; zone 'None' clears zoning (dezone). Generic names automatically resolve to the current map theme when a themed variant exists",
                stalenessWarning = LockStalenessWarning,
                zones,
            });
        }

        private BridgeResponse ZoneArea(BridgeRequest request)
        {
            if (!TryGetCity(out _, out BridgeResponse error))
            {
                return error;
            }

            if (!request.TryGetFloat("x", out float x) || !request.TryGetFloat("z", out float z))
            {
                return BridgeResponse.Error(BridgeErrorKind.InvalidArguments, "provide ?x=&z= center coordinates");
            }
            float radius = request.TryGetFloat("radius", out float rawRadius)
                ? math.clamp(rawRadius, kCellSize, 200f)
                : 32f;

            if (!TryResolveZone(request, out ZoneType targetZone, out string resolvedName, out error))
            {
                return error;
            }

            float2 center = new float2(x, z);
            BridgeToolSystem tool = World.GetOrCreateSystemManaged<BridgeToolSystem>();
            if (!tool.TryQueueZoneCircle(targetZone, resolvedName, center, radius, request))
            {
                return BridgeResponse.Error(BridgeErrorKind.Conflict, "another build operation is in progress, retry shortly");
            }
            // Completed asynchronously by BridgeToolSystem during ToolUpdate.
            return null;
        }

        private BridgeResponse ZoneRectangle(BridgeRequest request)
        {
            if (!TryGetCity(out _, out BridgeResponse error))
            {
                return error;
            }
            if (!request.TryGetFloat("x", out float x) || !request.TryGetFloat("z", out float z))
            {
                return BridgeResponse.Error(BridgeErrorKind.InvalidArguments, "provide ?x=&z= center coordinates");
            }
            if (!request.TryGetFloat("width", out float rawWidth)
                || !request.TryGetFloat("depth", out float rawDepth))
            {
                return BridgeResponse.Error(BridgeErrorKind.InvalidArguments, "provide ?width=&depth= rectangle dimensions in meters");
            }
            float width = math.clamp(rawWidth, kCellSize, 1000f);
            float depth = math.clamp(rawDepth, kCellSize, 1000f);
            request.TryGetFloat("rotation", out float rotationDegrees);
            if (!TryResolveZone(request, out ZoneType targetZone, out string resolvedName, out error))
            {
                return error;
            }

            BridgeToolSystem tool = World.GetOrCreateSystemManaged<BridgeToolSystem>();
            if (!tool.TryQueueZoneRectangle(
                    targetZone,
                    resolvedName,
                    new float2(x, z),
                    new float2(width, depth),
                    rotationDegrees,
                    request))
            {
                return BridgeResponse.Error(BridgeErrorKind.Conflict, "another build operation is in progress, retry shortly");
            }
            return null;
        }

        private bool TryResolveZone(
            BridgeRequest request,
            out ZoneType targetZone,
            out string resolvedName,
            out BridgeResponse error)
        {
            targetZone = ZoneType.None;
            resolvedName = null;
            error = null;
            if (!request.Query.TryGetValue("zone", out string zoneName) || string.IsNullOrEmpty(zoneName))
            {
                error = BridgeResponse.Error(BridgeErrorKind.InvalidArguments, "provide ?zone=<name from /zones, or 'None' to dezone>");
                return false;
            }
            if (string.Equals(zoneName, "None", StringComparison.OrdinalIgnoreCase))
            {
                resolvedName = "None";
                return true;
            }

            string lookupName = ResolveZoneNameForTheme(zoneName);
            if (!TryFindPrefabByName(ZonePrefabQuery, lookupName, out Entity zonePrefabEntity, out PrefabBase zonePrefab))
            {
                error = BridgeResponse.Error(BridgeErrorKind.NotFound, $"unknown zone '{zoneName}'; list via /zones");
                return false;
            }
            if (IsLocked(zonePrefabEntity))
            {
                error = BridgeResponse.Error(BridgeErrorKind.Conflict, $"zone '{zonePrefab.name}' is locked (milestone not reached)");
                return false;
            }
            targetZone = EntityManager.GetComponentData<ZoneData>(zonePrefabEntity).m_ZoneType;
            resolvedName = zonePrefab.name;
            return true;
        }

        /// <summary>
        /// The game exposes both generic zone prefabs and theme-specific
        /// growable zones. Generic residential/commercial cells can remain
        /// vacant forever because no growable building matches them. Keep the
        /// caller-facing vocabulary generic and select the current map's
        /// variant when it exists (for example Residential Low becomes NA
        /// Residential Low).
        /// </summary>
        private string ResolveZoneNameForTheme(string requestedName)
        {
            CityConfigurationSystem city =
                World.GetOrCreateSystemManaged<CityConfigurationSystem>();
            if (city.defaultTheme == Entity.Null)
            {
                return requestedName;
            }

            ThemePrefab theme = World.GetOrCreateSystemManaged<PrefabSystem>()
                .GetPrefab<ThemePrefab>(city.defaultTheme);
            if (theme == null || string.IsNullOrWhiteSpace(theme.assetPrefix))
            {
                return requestedName;
            }

            string themedName = theme.assetPrefix.Trim() + " " + requestedName;
            return TryFindPrefabByName(
                ZonePrefabQuery,
                themedName,
                out _,
                out _)
                ? themedName
                : requestedName;
        }

        /// <summary>
        /// Diagnostics: dump zone prefab metadata, per-block cell state and
        /// VacantLot buffers around a point. Used to root-cause why zoned
        /// residential cells never grow while industrial cells do.
        /// </summary>
        private BridgeResponse DebugZoneBlocks(BridgeRequest request)
        {
            if (!TryGetCity(out _, out BridgeResponse error))
            {
                return error;
            }
            if (!request.TryGetFloat("x", out float x) || !request.TryGetFloat("z", out float z))
            {
                return BridgeResponse.Error(BridgeErrorKind.InvalidArguments, "provide ?x=&z= center coordinates");
            }
            float radius = request.TryGetFloat("radius", out float rawRadius)
                ? math.clamp(rawRadius, 16f, 1000f)
                : 200f;
            float2 center = new float2(x, z);

            var zonePrefabs = new List<object>();
            using (NativeArray<Entity> entities = ZonePrefabQuery.ToEntityArray(Allocator.Temp))
            {
                foreach (Entity entity in entities)
                {
                    if (!EntityManager.HasComponent<ZoneData>(entity))
                    {
                        continue;
                    }
                    ZoneData zoneData = EntityManager.GetComponentData<ZoneData>(entity);
                    PrefabBase prefab = World.GetOrCreateSystemManaged<PrefabSystem>()
                        .GetPrefab<PrefabBase>(entity);
                    zonePrefabs.Add(new
                    {
                        name = prefab?.name,
                        zoneType = zoneData.m_ZoneType.ToString(),
                        areaType = zoneData.m_AreaType.ToString(),
                        hasZoneProperties = EntityManager.HasComponent<ZonePropertiesData>(entity),
                        hasProcessEstimates = EntityManager.HasBuffer<ProcessEstimate>(entity),
                    });
                }
            }

            var blocks = new List<object>();
            using (NativeArray<Entity> blockEntities = ZoneBlockQuery.ToEntityArray(Allocator.Temp))
            {
                foreach (Entity blockEntity in blockEntities)
                {
                    Block block = EntityManager.GetComponentData<Block>(blockEntity);
                    float blockExtent = kCellSize * (math.cmax(block.m_Size) + 1) * 0.71f;
                    if (math.distance(block.m_Position.xz, center) > radius + blockExtent)
                    {
                        continue;
                    }

                    DynamicBuffer<Cell> cells = EntityManager.GetBuffer<Cell>(blockEntity, isReadOnly: true);
                    var cellCounts = new Dictionary<string, int>();
                    var flagCounts = new Dictionary<string, int>();
                    var sampleCells = new List<object>();
                    int zonedCells = 0;
                    for (int i = 0; i < cells.Length; i++)
                    {
                        Cell cell = cells[i];
                        if (!cell.m_Zone.Equals(ZoneType.None))
                        {
                            zonedCells++;
                        }
                        string zoneKey = cell.m_Zone.ToString();
                        cellCounts[zoneKey] = cellCounts.TryGetValue(zoneKey, out int zoneCount)
                            ? zoneCount + 1
                            : 1;
                        string flagKey = cell.m_State.ToString();
                        flagCounts[flagKey] = flagCounts.TryGetValue(flagKey, out int flagCount)
                            ? flagCount + 1
                            : 1;
                        if (!cell.m_Zone.Equals(ZoneType.None) && sampleCells.Count < 12)
                        {
                            int cellX = i % block.m_Size.x;
                            int cellZ = i / block.m_Size.x;
                            float3 cellPosition = ZoneUtils.GetCellPosition(block, new int2(cellX, cellZ));
                            sampleCells.Add(new
                            {
                                x = cellPosition.x,
                                z = cellPosition.z,
                                zone = cell.m_Zone.ToString(),
                                state = cell.m_State.ToString(),
                                height = cell.m_Height,
                            });
                        }
                    }

                    var lots = new List<object>();
                    if (EntityManager.HasBuffer<VacantLot>(blockEntity))
                    {
                        DynamicBuffer<VacantLot> lotBuffer =
                            EntityManager.GetBuffer<VacantLot>(blockEntity, isReadOnly: true);
                        foreach (VacantLot lot in lotBuffer)
                        {
                            lots.Add(new
                            {
                                type = lot.m_Type.ToString(),
                                minX = lot.m_Area.x,
                                minZ = lot.m_Area.y,
                                maxX = lot.m_Area.z,
                                maxZ = lot.m_Area.w,
                                width = lot.m_Area.z - lot.m_Area.x,
                                depth = lot.m_Area.w - lot.m_Area.y,
                                height = lot.m_Height,
                                flags = lot.m_Flags.ToString(),
                            });
                        }
                    }

                    blocks.Add(new
                    {
                        entity = new { index = blockEntity.Index, version = blockEntity.Version },
                        position = new { x = block.m_Position.x, z = block.m_Position.z },
                        size = new { x = block.m_Size.x, z = block.m_Size.y },
                        cells = cells.Length,
                        zonedCells,
                        cellCounts,
                        flagCounts,
                        vacantLotCount = lots.Count,
                        lots,
                        sampleCells,
                        validArea = EntityManager.HasComponent<ValidArea>(blockEntity)
                            ? EntityManager.GetComponentData<ValidArea>(blockEntity).m_Area.ToString()
                            : null,
                    });
                }
            }

            ResidentialDemandSystem residential = World.GetOrCreateSystemManaged<ResidentialDemandSystem>();
            CommercialDemandSystem commercial = World.GetOrCreateSystemManaged<CommercialDemandSystem>();
            IndustrialDemandSystem industrial = World.GetOrCreateSystemManaged<IndustrialDemandSystem>();

            var outsideConnections = new List<object>();
            using (EntityQuery outsideQuery = EntityManager.CreateEntityQuery(
                ComponentType.ReadOnly<Game.Objects.OutsideConnection>()))
            using (NativeArray<Entity> outsideEntities = outsideQuery.ToEntityArray(Allocator.Temp))
            {
                foreach (Entity entity in outsideEntities)
                {
                    outsideConnections.Add(new
                    {
                        entity = new { index = entity.Index, version = entity.Version },
                        position = EntityManager.HasComponent<Game.Objects.Transform>(entity)
                            ? EntityManager.GetComponentData<Game.Objects.Transform>(entity).m_Position.ToString()
                            : null,
                        prefab = EntityManager.HasComponent<Game.Prefabs.PrefabRef>(entity)
                            ? EntityManager.GetComponentData<Game.Prefabs.PrefabRef>(entity).m_Prefab.ToString()
                            : null,
                    });
                }
            }

            return BridgeResponse.Json(new
            {
                scope = new { x, z, radius },
                zonePrefabs,
                blocks,
                outsideConnections,
                demand = new
                {
                    residential = new
                    {
                        householdDemand = residential.householdDemand,
                        buildingDemand = new
                        {
                            low = residential.buildingDemand.x,
                            medium = residential.buildingDemand.y,
                            high = residential.buildingDemand.z,
                        },
                    },
                    commercial = commercial.buildingDemand,
                    industrial = industrial.industrialBuildingDemand,
                    office = industrial.officeBuildingDemand,
                    storage = industrial.storageBuildingDemand,
                },
            });
        }
    }
}
