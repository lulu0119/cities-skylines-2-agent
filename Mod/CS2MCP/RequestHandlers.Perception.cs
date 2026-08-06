using System;
using System.Collections.Generic;
using Game;
using Game.Notifications;
using Game.Prefabs;
using Game.Rendering;
using Game.Simulation;
using Game.Zones;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

namespace CS2MCP
{
    /// <summary>
    /// Perception endpoints: camera control (for AI-directed screenshots),
    /// terrain/water export, cell-map grids (land value, pollution, ground
    /// water), zoning readback, warning-icon listing and entity inspection.
    /// </summary>
    public sealed partial class RequestHandlers
    {
        private const float kWorldHalfSize = 7168f; // CellMapSystem.kMapSize / 2
        /// <summary>Fixed sample lattice for terrain / gridmap area queries.</summary>
        private const int kAreaSampleGrid = 8;

        private EntityQuery m_IconQuery;
        private bool m_IconQueryCreated;

        private EntityQuery IconQuery
        {
            get
            {
                if (!m_IconQueryCreated)
                {
                    m_IconQuery = EntityManager.CreateEntityQuery(new EntityQueryDesc
                    {
                        All = new[]
                        {
                            ComponentType.ReadOnly<Icon>(),
                            ComponentType.ReadOnly<PrefabRef>(),
                        },
                        None = new[]
                        {
                            ComponentType.ReadOnly<Game.Tools.Temp>(),
                            ComponentType.ReadOnly<Game.Common.Deleted>(),
                        },
                    });
                    m_IconQueryCreated = true;
                }
                return m_IconQuery;
            }
        }

        private BridgeResponse GetCamera()
        {
            CameraUpdateSystem cameraSystem = World.GetOrCreateSystemManaged<CameraUpdateSystem>();
            CameraController controller = cameraSystem.gamePlayController;
            if (controller == null)
            {
                return BridgeResponse.Error(503, "gameplay camera controller not available (still loading?)");
            }
            return BridgeResponse.Json(new
            {
                pivot = new { x = controller.pivot.x, y = controller.pivot.y, z = controller.pivot.z },
                position = new { x = controller.position.x, y = controller.position.y, z = controller.position.z },
                angle = new { x = controller.angle.x, y = controller.angle.y },
                zoom = controller.zoom,
                note = "pivot = look-at point; angle.x = compass rotation deg, angle.y = tilt deg; zoom = distance",
            });
        }

        private BridgeResponse SetCamera(BridgeRequest request)
        {
            CameraUpdateSystem cameraSystem = World.GetOrCreateSystemManaged<CameraUpdateSystem>();
            CameraController controller = cameraSystem.gamePlayController;
            if (controller == null)
            {
                return BridgeResponse.Error(503, "gameplay camera controller not available (still loading?)");
            }

            bool changed = false;
            bool hasX = request.TryGetFloat("x", out float x);
            bool hasZ = request.TryGetFloat("z", out float z);
            if (hasX && hasZ)
            {
                float y;
                if (!request.TryGetFloat("y", out y))
                {
                    TerrainSystem terrain = World.GetOrCreateSystemManaged<TerrainSystem>();
                    TerrainHeightData heightData = terrain.GetHeightData();
                    y = TerrainUtils.SampleHeight(ref heightData, new float3(x, 0f, z));
                }
                controller.pivot = new Vector3(x, y, z);
                changed = true;
            }
            if (request.TryGetFloat("angleX", out float angleX) | request.TryGetFloat("angleY", out float angleY))
            {
                float2 angle = controller.angle;
                if (request.Query.ContainsKey("angleX"))
                {
                    angle.x = angleX;
                }
                if (request.Query.ContainsKey("angleY"))
                {
                    angle.y = math.clamp(angleY, 0f, 89f);
                }
                controller.angle = angle;
                changed = true;
            }
            if (request.TryGetFloat("zoom", out float zoom))
            {
                controller.zoom = math.clamp(zoom, 10f, 10000f);
                changed = true;
            }

            if (!changed)
            {
                return BridgeResponse.Error(400, "provide at least one of: x&z (pivot, y optional), angleX, angleY, zoom");
            }
            return GetCamera();
        }

        private BridgeResponse GetTerrain(BridgeRequest request)
        {
            if (!TryGetCity(out _, out BridgeResponse error))
            {
                return error;
            }
            if (!TryGetWorldBounds(request, out float xMin, out float zMin, out float xMax, out float zMax, out BridgeResponse boundsError))
            {
                return boundsError;
            }

            TerrainSystem terrain = World.GetOrCreateSystemManaged<TerrainSystem>();
            TerrainHeightData heightData = terrain.GetHeightData();
            WaterSystem water = World.GetOrCreateSystemManaged<WaterSystem>();
            WaterSurfaceData<SurfaceWater> surfaceData = water.GetSurfaceData(out JobHandle waterDeps);
            waterDeps.Complete();

            var samples = new List<object>(kAreaSampleGrid * kAreaSampleGrid);
            float heightMin = float.PositiveInfinity;
            float heightMax = float.NegativeInfinity;
            double heightSum = 0;
            int waterCount = 0;

            for (int row = 0; row < kAreaSampleGrid; row++)
            {
                float tz = (row + 0.5f) / kAreaSampleGrid;
                float worldZ = math.lerp(zMin, zMax, tz);
                for (int col = 0; col < kAreaSampleGrid; col++)
                {
                    float tx = (col + 0.5f) / kAreaSampleGrid;
                    float worldX = math.lerp(xMin, xMax, tx);
                    var samplePosition = new float3(worldX, 0f, worldZ);
                    float height = (float)Math.Round(TerrainUtils.SampleHeight(ref heightData, samplePosition), 1);
                    float depth = WaterUtils.SampleDepth(ref surfaceData, samplePosition);
                    bool hasWater = depth > 0.05f;
                    if (hasWater)
                    {
                        waterCount++;
                        depth = (float)Math.Round(depth, 1);
                    }
                    else
                    {
                        depth = 0f;
                    }

                    heightMin = math.min(heightMin, height);
                    heightMax = math.max(heightMax, height);
                    heightSum += height;
                    samples.Add(new
                    {
                        x = (float)Math.Round(worldX, 1),
                        z = (float)Math.Round(worldZ, 1),
                        height,
                        water = hasWater,
                        waterDepth = depth,
                    });
                }
            }

            int n = samples.Count;
            return BridgeResponse.Json(new
            {
                bounds = new { xMin, zMin, xMax, zMax },
                sampleGrid = kAreaSampleGrid,
                sampleCount = n,
                height = new
                {
                    min = heightMin,
                    max = heightMax,
                    mean = (float)Math.Round(heightSum / n, 1),
                },
                waterCoverage = (float)Math.Round(waterCount / (float)n, 3),
                note = "fixed 8x8 uniform samples inside bounds (row-major, z varies slowest); no full heightmap arrays",
                samples,
            });
        }

        private BridgeResponse GetGridMap(BridgeRequest request)
        {
            if (!TryGetCity(out _, out BridgeResponse error))
            {
                return error;
            }
            if (!request.Query.TryGetValue("layer", out string layer) || string.IsNullOrEmpty(layer))
            {
                return BridgeResponse.Error(400,
                    "provide ?layer=landValue|groundPollution|airPollution|noisePollution|groundWater|groundWaterPollution");
            }
            if (!TryGetWorldBounds(request, out float xMin, out float zMin, out float xMax, out float zMax, out BridgeResponse boundsError))
            {
                return boundsError;
            }

            JobHandle deps;
            Func<int, float> selector;
            int sourceSize;
            string unit;
            switch (layer.ToLowerInvariant())
            {
                case "landvalue":
                {
                    NativeArray<LandValueCell> map = World.GetOrCreateSystemManaged<LandValueSystem>().GetMap(readOnly: true, out deps);
                    deps.Complete();
                    sourceSize = (int)math.round(math.sqrt(map.Length));
                    selector = i => map[i].m_LandValue;
                    unit = "land value per cell";
                    break;
                }
                case "groundpollution":
                {
                    NativeArray<GroundPollution> map = World.GetOrCreateSystemManaged<GroundPollutionSystem>().GetMap(readOnly: true, out deps);
                    deps.Complete();
                    sourceSize = (int)math.round(math.sqrt(map.Length));
                    selector = i => map[i].m_Pollution;
                    unit = "pollution amount";
                    break;
                }
                case "airpollution":
                {
                    NativeArray<AirPollution> map = World.GetOrCreateSystemManaged<AirPollutionSystem>().GetMap(readOnly: true, out deps);
                    deps.Complete();
                    sourceSize = (int)math.round(math.sqrt(map.Length));
                    selector = i => map[i].m_Pollution;
                    unit = "pollution amount";
                    break;
                }
                case "noisepollution":
                {
                    NativeArray<NoisePollution> map = World.GetOrCreateSystemManaged<NoisePollutionSystem>().GetMap(readOnly: true, out deps);
                    deps.Complete();
                    sourceSize = (int)math.round(math.sqrt(map.Length));
                    selector = i => map[i].m_Pollution;
                    unit = "noise amount";
                    break;
                }
                case "groundwater":
                {
                    NativeArray<GroundWater> map = World.GetOrCreateSystemManaged<GroundWaterSystem>().GetMap(readOnly: true, out deps);
                    deps.Complete();
                    sourceSize = (int)math.round(math.sqrt(map.Length));
                    selector = i => map[i].m_Amount;
                    unit = "ground water amount";
                    break;
                }
                case "groundwaterpollution":
                {
                    NativeArray<GroundWater> map = World.GetOrCreateSystemManaged<GroundWaterSystem>().GetMap(readOnly: true, out deps);
                    deps.Complete();
                    sourceSize = (int)math.round(math.sqrt(map.Length));
                    selector = i => map[i].m_Polluted;
                    unit = "polluted ground water amount";
                    break;
                }
                default:
                    return BridgeResponse.Error(400, $"unknown layer '{layer}'");
            }

            float cellSize = kWorldHalfSize * 2f / sourceSize;
            int cellMinX = math.clamp((int)math.floor((xMin + kWorldHalfSize) / cellSize), 0, sourceSize - 1);
            int cellMaxX = math.clamp((int)math.floor((xMax + kWorldHalfSize) / cellSize), 0, sourceSize - 1);
            int cellMinZ = math.clamp((int)math.floor((zMin + kWorldHalfSize) / cellSize), 0, sourceSize - 1);
            int cellMaxZ = math.clamp((int)math.floor((zMax + kWorldHalfSize) / cellSize), 0, sourceSize - 1);
            if (cellMinX > cellMaxX)
            {
                (cellMinX, cellMaxX) = (cellMaxX, cellMinX);
            }
            if (cellMinZ > cellMaxZ)
            {
                (cellMinZ, cellMaxZ) = (cellMaxZ, cellMinZ);
            }

            int cellsInBounds = (cellMaxX - cellMinX + 1) * (cellMaxZ - cellMinZ + 1);
            const int hardMaxCells = 128;
            bool truncated = cellsInBounds > hardMaxCells;

            var samples = new List<object>(kAreaSampleGrid * kAreaSampleGrid);
            float valueMin = float.PositiveInfinity;
            float valueMax = float.NegativeInfinity;
            double valueSum = 0;
            int nonzero = 0;

            for (int row = 0; row < kAreaSampleGrid; row++)
            {
                float tz = (row + 0.5f) / kAreaSampleGrid;
                float worldZ = math.lerp(zMin, zMax, tz);
                for (int col = 0; col < kAreaSampleGrid; col++)
                {
                    float tx = (col + 0.5f) / kAreaSampleGrid;
                    float worldX = math.lerp(xMin, xMax, tx);
                    int cellX = math.clamp((int)math.floor((worldX + kWorldHalfSize) / cellSize), 0, sourceSize - 1);
                    int cellZ = math.clamp((int)math.floor((worldZ + kWorldHalfSize) / cellSize), 0, sourceSize - 1);
                    int index = cellZ * sourceSize + cellX;
                    float value = (float)Math.Round(selector(index), 2);
                    valueMin = math.min(valueMin, value);
                    valueMax = math.max(valueMax, value);
                    valueSum += value;
                    if (value > 0.001f)
                    {
                        nonzero++;
                    }
                    samples.Add(new
                    {
                        x = (float)Math.Round(worldX, 1),
                        z = (float)Math.Round(worldZ, 1),
                        cellX,
                        cellZ,
                        value,
                    });
                }
            }

            int n = samples.Count;
            return BridgeResponse.Json(new
            {
                layer,
                unit,
                bounds = new { xMin, zMin, xMax, zMax },
                nativeTextureSize = sourceSize,
                cellSize,
                cellsInBounds,
                sampleGrid = kAreaSampleGrid,
                sampleCount = n,
                truncated,
                warning = truncated
                    ? $"范围覆盖 {cellsInBounds} 个原生格子，超过上限 {hardMaxCells}；已改为固定 {kAreaSampleGrid}x{kAreaSampleGrid} 均匀采样。缩小范围可提高密度。"
                    : null,
                value = new
                {
                    min = valueMin,
                    max = valueMax,
                    mean = (float)Math.Round(valueSum / n, 2),
                    nonzeroSamples = nonzero,
                },
                note = "fixed 8x8 uniform samples inside bounds; full cell arrays are never returned",
                samples,
            });
        }

        /// <summary>
        /// Accepts x+z+radius or xMin+zMin+xMax+zMax. Required for area perception tools.
        /// </summary>
        private static bool TryGetWorldBounds(
            BridgeRequest request,
            out float xMin,
            out float zMin,
            out float xMax,
            out float zMax,
            out BridgeResponse error)
        {
            xMin = zMin = xMax = zMax = 0f;
            error = null;

            bool hasBox = request.TryGetFloat("xMin", out xMin)
                & request.TryGetFloat("zMin", out zMin)
                & request.TryGetFloat("xMax", out xMax)
                & request.TryGetFloat("zMax", out zMax);
            if (!hasBox)
            {
                if (!(request.TryGetFloat("x", out float x)
                      & request.TryGetFloat("z", out float z)
                      & request.TryGetFloat("radius", out float radius)))
                {
                    error = BridgeResponse.Error(400,
                        "provide a map range: x&z&radius OR xMin&zMin&xMax&zMax");
                    return false;
                }
                radius = math.clamp(radius, 8f, kWorldHalfSize * 2f);
                xMin = x - radius;
                zMin = z - radius;
                xMax = x + radius;
                zMax = z + radius;
            }

            if (xMin > xMax)
            {
                (xMin, xMax) = (xMax, xMin);
            }
            if (zMin > zMax)
            {
                (zMin, zMax) = (zMax, zMin);
            }
            xMin = math.clamp(xMin, -kWorldHalfSize, kWorldHalfSize);
            xMax = math.clamp(xMax, -kWorldHalfSize, kWorldHalfSize);
            zMin = math.clamp(zMin, -kWorldHalfSize, kWorldHalfSize);
            zMax = math.clamp(zMax, -kWorldHalfSize, kWorldHalfSize);
            if (xMax - xMin < 1f || zMax - zMin < 1f)
            {
                error = BridgeResponse.Error(400, "range too small; need at least 1m on each axis");
                return false;
            }
            return true;
        }

        private BridgeResponse GetZoning(BridgeRequest request)
        {
            if (!TryGetCity(out _, out BridgeResponse error))
            {
                return error;
            }

            bool hasCenter = request.TryGetFloat("x", out float x) & request.TryGetFloat("z", out float z);
            float radius = request.TryGetFloat("radius", out float rawRadius) ? math.max(rawRadius, 8f) : float.MaxValue;
            float2 center = new float2(x, z);

            // zone type index -> prefab name
            var zoneNames = new Dictionary<ushort, string>();
            PrefabSystem prefabSystem = World.GetOrCreateSystemManaged<PrefabSystem>();
            using (NativeArray<Entity> zonePrefabs = ZonePrefabQuery.ToEntityArray(Allocator.Temp))
            {
                foreach (Entity entity in zonePrefabs)
                {
                    PrefabBase prefab = prefabSystem.GetPrefab<PrefabBase>(entity);
                    if (prefab != null)
                    {
                        zoneNames[EntityManager.GetComponentData<ZoneData>(entity).m_ZoneType.m_Index] = prefab.name;
                    }
                }
            }

            var byZone = new Dictionary<string, int[]>(); // name -> [cells, occupied]
            int totalVisible = 0;
            int totalZoned = 0;
            using (NativeArray<Entity> blocks = ZoneBlockQuery.ToEntityArray(Allocator.Temp))
            {
                foreach (Entity blockEntity in blocks)
                {
                    Block block = EntityManager.GetComponentData<Block>(blockEntity);
                    if (hasCenter && radius < float.MaxValue)
                    {
                        float blockExtent = kCellSize * (math.cmax(block.m_Size) + 1) * 0.71f;
                        if (math.distance(block.m_Position.xz, center) > radius + blockExtent)
                        {
                            continue;
                        }
                    }
                    DynamicBuffer<Cell> cells = EntityManager.GetBuffer<Cell>(blockEntity, isReadOnly: true);
                    for (int i = 0; i < cells.Length; i++)
                    {
                        Cell cell = cells[i];
                        if ((cell.m_State & CellFlags.Visible) == 0)
                        {
                            continue;
                        }
                        if (hasCenter && radius < float.MaxValue)
                        {
                            int2 cellIndex = new int2(i % block.m_Size.x, i / block.m_Size.x);
                            if (math.distance(ZoneUtils.GetCellPosition(block, cellIndex).xz, center) > radius)
                            {
                                continue;
                            }
                        }
                        totalVisible++;
                        if (cell.m_Zone.Equals(ZoneType.None))
                        {
                            continue;
                        }
                        totalZoned++;
                        string name = zoneNames.TryGetValue(cell.m_Zone.m_Index, out string zoneName) ? zoneName : $"<index {cell.m_Zone.m_Index}>";
                        if (!byZone.TryGetValue(name, out int[] counts))
                        {
                            counts = new int[2];
                            byZone[name] = counts;
                        }
                        counts[0]++;
                        if ((cell.m_State & CellFlags.Occupied) != 0)
                        {
                            counts[1]++;
                        }
                    }
                }
            }

            var zones = new Dictionary<string, object>();
            foreach (KeyValuePair<string, int[]> pair in byZone)
            {
                zones[pair.Key] = new { cells = pair.Value[0], occupied = pair.Value[1], empty = pair.Value[0] - pair.Value[1] };
            }

            return BridgeResponse.Json(new
            {
                scope = hasCenter && radius < float.MaxValue ? $"radius {radius} around ({x}, {z})" : "whole city",
                zonableCells = totalVisible,
                zonedCells = totalZoned,
                note = "empty zoned cells grow buildings while the simulation runs if demand exists",
                byZone = zones,
            });
        }

        private BridgeResponse GetNotifications(BridgeRequest request)
        {
            if (!TryGetCity(out _, out BridgeResponse error))
            {
                return error;
            }

            int limit = request.TryGetInt("limit", out int rawLimit) ? math.clamp(rawLimit, 1, 500) : 100;
            PrefabSystem prefabSystem = World.GetOrCreateSystemManaged<PrefabSystem>();

            var counts = new Dictionary<string, int>();
            var items = new List<object>();
            int total = 0;
            using (NativeArray<Entity> icons = IconQuery.ToEntityArray(Allocator.Temp))
            {
                foreach (Entity iconEntity in icons)
                {
                    Icon icon = EntityManager.GetComponentData<Icon>(iconEntity);
                    PrefabBase prefab = prefabSystem.GetPrefab<PrefabBase>(EntityManager.GetComponentData<PrefabRef>(iconEntity).m_Prefab);
                    string type = prefab != null ? prefab.name : "<unknown>";
                    total++;
                    counts[type] = counts.TryGetValue(type, out int c) ? c + 1 : 1;
                    if (items.Count < limit)
                    {
                        object target = null;
                        if (EntityManager.HasComponent<Game.Common.Owner>(iconEntity))
                        {
                            Entity owner = EntityManager.GetComponentData<Game.Common.Owner>(iconEntity).m_Owner;
                            string ownerPrefab = null;
                            if (EntityManager.HasComponent<PrefabRef>(owner))
                            {
                                PrefabBase op = prefabSystem.GetPrefab<PrefabBase>(EntityManager.GetComponentData<PrefabRef>(owner).m_Prefab);
                                ownerPrefab = op != null ? op.name : null;
                            }
                            target = new { index = owner.Index, version = owner.Version, prefab = ownerPrefab };
                        }
                        items.Add(new
                        {
                            type,
                            priority = (int)icon.m_Priority,
                            location = new { x = icon.m_Location.x, y = icon.m_Location.y, z = icon.m_Location.z },
                            target,
                        });
                    }
                }
            }

            return BridgeResponse.Json(new
            {
                total,
                returned = items.Count,
                countsByType = counts,
                note = "in-world warning icons (no electricity/water, garbage piling, abandoned...); use target with /entity/inspect",
                notifications = items,
            });
        }

        private BridgeResponse InspectEntity(BridgeRequest request)
        {
            if (!TryGetCity(out _, out BridgeResponse error))
            {
                return error;
            }
            if (!request.TryGetInt("index", out int index) || !request.TryGetInt("version", out int version))
            {
                return BridgeResponse.Error(400, "provide ?index=<int>&version=<int>");
            }
            var entity = new Entity { Index = index, Version = version };
            if (!EntityManager.Exists(entity))
            {
                return BridgeResponse.Error(404, $"entity {index}:{version} does not exist");
            }

            PrefabSystem prefabSystem = World.GetOrCreateSystemManaged<PrefabSystem>();
            var result = new Dictionary<string, object>
            {
                ["entity"] = new { index, version },
            };

            if (EntityManager.HasComponent<PrefabRef>(entity))
            {
                PrefabBase prefab = prefabSystem.GetPrefab<PrefabBase>(EntityManager.GetComponentData<PrefabRef>(entity).m_Prefab);
                result["prefab"] = prefab != null ? prefab.name : null;
            }
            if (EntityManager.HasComponent<Game.Objects.Transform>(entity))
            {
                Game.Objects.Transform transform = EntityManager.GetComponentData<Game.Objects.Transform>(entity);
                result["position"] = new { x = transform.m_Position.x, y = transform.m_Position.y, z = transform.m_Position.z };
            }

            var flags = new List<string>();
            if (EntityManager.HasComponent<Game.Buildings.Building>(entity)) flags.Add("building");
            if (EntityManager.HasComponent<Game.Net.Edge>(entity)) flags.Add("roadSegment");
            if (EntityManager.HasComponent<Game.Buildings.Abandoned>(entity)) flags.Add("abandoned");
            if (EntityManager.HasComponent<Game.Buildings.Condemned>(entity)) flags.Add("condemned");
            if (EntityManager.HasComponent<Game.Common.Destroyed>(entity)) flags.Add("destroyed");
            if (EntityManager.HasComponent<Game.Common.Owner>(entity)) flags.Add("hasOwner");
            result["flags"] = flags;

            if (EntityManager.HasBuffer<Game.Buildings.Renter>(entity))
            {
                DynamicBuffer<Game.Buildings.Renter> renters = EntityManager.GetBuffer<Game.Buildings.Renter>(entity, isReadOnly: true);
                var renterInfos = new List<object>();
                for (int i = 0; i < renters.Length && i < 20; i++)
                {
                    Entity renter = renters[i].m_Renter;
                    string renterPrefab = null;
                    if (EntityManager.HasComponent<PrefabRef>(renter))
                    {
                        PrefabBase rp = prefabSystem.GetPrefab<PrefabBase>(EntityManager.GetComponentData<PrefabRef>(renter).m_Prefab);
                        renterPrefab = rp != null ? rp.name : null;
                    }
                    int citizens = EntityManager.HasBuffer<Game.Citizens.HouseholdCitizen>(renter)
                        ? EntityManager.GetBuffer<Game.Citizens.HouseholdCitizen>(renter, isReadOnly: true).Length
                        : 0;
                    int employees = EntityManager.HasBuffer<Game.Companies.Employee>(renter)
                        ? EntityManager.GetBuffer<Game.Companies.Employee>(renter, isReadOnly: true).Length
                        : 0;
                    renterInfos.Add(new { prefab = renterPrefab, citizens, employees });
                }
                result["renterCount"] = renters.Length;
                result["renters"] = renterInfos;
            }

            if (EntityManager.HasBuffer<Game.Companies.Employee>(entity))
            {
                result["employees"] = EntityManager.GetBuffer<Game.Companies.Employee>(entity, isReadOnly: true).Length;
            }

            return BridgeResponse.Json(result);
        }
    }
}
