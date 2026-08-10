using System;
using System.Collections.Generic;
using Colossal.Mathematics;
using Game.Prefabs;
using Game.Simulation;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Transform = Game.Objects.Transform;

namespace CS2MCP
{
    /// <summary>
    /// Construction endpoints: prefab search, building placement (via
    /// BridgeToolSystem), placed-building listing and demolition.
    /// </summary>
    public sealed partial class RequestHandlers
    {
        private const float kFootprintCellSize = 8f;

        private EntityQuery m_BuildingPrefabQuery;
        private bool m_BuildingPrefabQueryCreated;
        private EntityQuery m_RoadPrefabQuery;
        private bool m_RoadPrefabQueryCreated;
        private EntityQuery m_PlacedBuildingQuery;
        private bool m_PlacedBuildingQueryCreated;
        private EntityQuery m_PlacedRoadQuery;
        private bool m_PlacedRoadQueryCreated;
        private EntityQuery m_NetPrefabQuery;
        private bool m_NetPrefabQueryCreated;
        private EntityQuery m_TreePrefabQuery;
        private bool m_TreePrefabQueryCreated;

        private EntityQuery NetPrefabQuery
        {
            get
            {
                if (!m_NetPrefabQueryCreated)
                {
                    m_NetPrefabQuery = EntityManager.CreateEntityQuery(
                        ComponentType.ReadOnly<PrefabData>(),
                        ComponentType.ReadOnly<NetGeometryData>());
                    m_NetPrefabQueryCreated = true;
                }
                return m_NetPrefabQuery;
            }
        }

        private EntityQuery TreePrefabQuery
        {
            get
            {
                if (!m_TreePrefabQueryCreated)
                {
                    m_TreePrefabQuery = EntityManager.CreateEntityQuery(
                        ComponentType.ReadOnly<PrefabData>(),
                        ComponentType.ReadOnly<TreeData>());
                    m_TreePrefabQueryCreated = true;
                }
                return m_TreePrefabQuery;
            }
        }

        private EntityQuery BuildingPrefabQuery
        {
            get
            {
                if (!m_BuildingPrefabQueryCreated)
                {
                    m_BuildingPrefabQuery = EntityManager.CreateEntityQuery(
                        ComponentType.ReadOnly<PrefabData>(),
                        ComponentType.ReadOnly<BuildingData>());
                    m_BuildingPrefabQueryCreated = true;
                }
                return m_BuildingPrefabQuery;
            }
        }

        private EntityQuery RoadPrefabQuery
        {
            get
            {
                if (!m_RoadPrefabQueryCreated)
                {
                    m_RoadPrefabQuery = EntityManager.CreateEntityQuery(
                        ComponentType.ReadOnly<PrefabData>(),
                        ComponentType.ReadOnly<RoadData>());
                    m_RoadPrefabQueryCreated = true;
                }
                return m_RoadPrefabQuery;
            }
        }

        private EntityQuery PlacedBuildingQuery
        {
            get
            {
                if (!m_PlacedBuildingQueryCreated)
                {
                    m_PlacedBuildingQuery = EntityManager.CreateEntityQuery(new EntityQueryDesc
                    {
                        All = new[]
                        {
                            ComponentType.ReadOnly<Game.Buildings.Building>(),
                            ComponentType.ReadOnly<Transform>(),
                            ComponentType.ReadOnly<PrefabRef>(),
                        },
                        None = new[]
                        {
                            ComponentType.ReadOnly<Game.Tools.Temp>(),
                            ComponentType.ReadOnly<Game.Common.Deleted>(),
                        },
                    });
                    m_PlacedBuildingQueryCreated = true;
                }
                return m_PlacedBuildingQuery;
            }
        }

        private EntityQuery PlacedRoadQuery
        {
            get
            {
                if (!m_PlacedRoadQueryCreated)
                {
                    m_PlacedRoadQuery = EntityManager.CreateEntityQuery(new EntityQueryDesc
                    {
                        All = new[]
                        {
                            ComponentType.ReadOnly<Game.Net.Edge>(),
                            ComponentType.ReadOnly<Game.Net.Curve>(),
                            ComponentType.ReadOnly<PrefabRef>(),
                        },
                        None = new[]
                        {
                            ComponentType.ReadOnly<Game.Tools.Temp>(),
                            ComponentType.ReadOnly<Game.Common.Deleted>(),
                            ComponentType.ReadOnly<Game.Common.Owner>(),
                        },
                    });
                    m_PlacedRoadQueryCreated = true;
                }
                return m_PlacedRoadQuery;
            }
        }

        private BridgeResponse ListRoads(BridgeRequest request)
        {
            if (!TryGetCity(out _, out BridgeResponse error))
            {
                return error;
            }

            request.Query.TryGetValue("query", out string search);
            const int hardMax = 128;
            int limit = request.TryGetInt("limit", out int rawLimit) ? math.clamp(rawLimit, 1, hardMax) : hardMax;
            bool hasCenter = request.TryGetFloat("x", out float x) & request.TryGetFloat("z", out float z);
            float radius = request.TryGetFloat("radius", out float rawRadius) ? math.max(rawRadius, 1f) : 250f;
            float2 center = new float2(x, z);

            PrefabSystem prefabSystem = World.GetOrCreateSystemManaged<PrefabSystem>();
            var found = new List<(float distance, object item)>();
            int total = 0;
            using (NativeArray<Entity> entities = PlacedRoadQuery.ToEntityArray(Allocator.Temp))
            {
                foreach (Entity entity in entities)
                {
                    Game.Net.Curve curve = EntityManager.GetComponentData<Game.Net.Curve>(entity);
                    float2 midpoint = (curve.m_Bezier.a.xz + curve.m_Bezier.d.xz) * 0.5f;
                    if (hasCenter && math.distance(midpoint, center) > radius)
                    {
                        continue;
                    }
                    PrefabRef prefabRef = EntityManager.GetComponentData<PrefabRef>(entity);
                    PrefabBase prefab = prefabSystem.GetPrefab<PrefabBase>(prefabRef.m_Prefab);
                    string name = prefab != null ? prefab.name : "<unknown>";
                    if (!string.IsNullOrEmpty(search)
                        && name.IndexOf(search, StringComparison.OrdinalIgnoreCase) < 0)
                    {
                        continue;
                    }
                    total++;
                    float distance = hasCenter ? math.distance(midpoint, center) : 0f;
                    var item = new
                    {
                        entity = new { index = entity.Index, version = entity.Version },
                        prefab = name,
                        start = new { x = curve.m_Bezier.a.x, z = curve.m_Bezier.a.z },
                        end = new { x = curve.m_Bezier.d.x, z = curve.m_Bezier.d.z },
                        length = curve.m_Length,
                        widthM = NetworkWidthM(EntityManager, prefabRef.m_Prefab),
                        distanceM = hasCenter ? (double?)Math.Round(distance, 1) : null,
                    };
                    if (found.Count < limit)
                    {
                        found.Add((distance, item));
                    }
                    else if (hasCenter)
                    {
                        int worst = 0;
                        for (int j = 1; j < found.Count; j++)
                        {
                            if (found[j].distance > found[worst].distance)
                            {
                                worst = j;
                            }
                        }
                        if (distance < found[worst].distance)
                        {
                            found[worst] = (distance, item);
                        }
                    }
                }
            }

            if (hasCenter && found.Count > 1)
            {
                for (int i = 0; i < found.Count - 1; i++)
                {
                    for (int j = i + 1; j < found.Count; j++)
                    {
                        if (found[j].distance < found[i].distance)
                        {
                            (found[i], found[j]) = (found[j], found[i]);
                        }
                    }
                }
            }
            var results = new List<object>(found.Count);
            foreach ((_, object item) in found)
            {
                results.Add(item);
            }

            bool truncated = total > results.Count;
            return BridgeResponse.Json(new
            {
                totalMatches = total,
                returned = results.Count,
                limit,
                truncated,
                warning = truncated
                    ? $"too many results: {total} road segments match, only {results.Count} returned; shrink radius / add query filter, or paginate."
                    : null,
                note = "one entry per road segment (edge/curve); hard max 128; sorted by distanceM when x/z given; use entity index+version with /build/demolish",
                roads = results,
            });
        }

        private BridgeResponse GetPrefabs(BridgeRequest request)
        {
            if (!TryGetCity(out _, out BridgeResponse error))
            {
                return error;
            }

            string category = request.Query.TryGetValue("category", out string rawCategory)
                ? rawCategory.ToLowerInvariant()
                : "building";
            EntityQuery query;
            switch (category)
            {
                case "building":
                    query = BuildingPrefabQuery;
                    break;
                case "road":
                    query = RoadPrefabQuery;
                    break;
                case "net":
                    query = NetPrefabQuery;
                    break;
                case "tree":
                    query = TreePrefabQuery;
                    break;
                default:
                    return BridgeResponse.Error(400, "category must be 'building', 'road', 'net' (all networks incl. pipes/power/tracks/paths) or 'tree'");
            }

            request.Query.TryGetValue("query", out string search);
            int limit = request.TryGetInt("limit", out int rawLimit) ? math.clamp(rawLimit, 1, 200) : 50;

            PrefabSystem prefabSystem = World.GetOrCreateSystemManaged<PrefabSystem>();
            var results = new List<object>();
            int total = 0;
            using (NativeArray<Entity> entities = query.ToEntityArray(Allocator.Temp))
            {
                foreach (Entity entity in entities)
                {
                    PrefabBase prefab = prefabSystem.GetPrefab<PrefabBase>(entity);
                    if (prefab == null)
                    {
                        continue;
                    }
                    if (!string.IsNullOrEmpty(search)
                        && prefab.name.IndexOf(search, StringComparison.OrdinalIgnoreCase) < 0)
                    {
                        continue;
                    }
                    total++;
                    if (results.Count < limit)
                    {
                        object lotSize = null;
                        object footprintMeters = null;
                        float? widthM = null;
                        if (EntityManager.HasComponent<BuildingData>(entity))
                        {
                            int2 lot = EntityManager.GetComponentData<BuildingData>(entity).m_LotSize;
                            lotSize = new { x = lot.x, z = lot.y };
                            footprintMeters = new
                            {
                                x = (float)Math.Round(lot.x * kFootprintCellSize, 1),
                                z = (float)Math.Round(lot.y * kFootprintCellSize, 1),
                            };
                        }
                        else if (EntityManager.HasComponent<ObjectGeometryData>(entity))
                        {
                            float3 size = EntityManager.GetComponentData<ObjectGeometryData>(entity).m_Size;
                            footprintMeters = new
                            {
                                x = (float)Math.Round(size.x, 1),
                                z = (float)Math.Round(size.z, 1),
                            };
                        }
                        if (EntityManager.HasComponent<NetGeometryData>(entity))
                        {
                            widthM = EntityManager.GetComponentData<NetGeometryData>(entity).m_DefaultWidth;
                        }
                        results.Add(new
                        {
                            name = prefab.name,
                            type = prefab.GetType().Name,
                            locked = IsLocked(entity),
                            lotSize,
                            footprintMeters,
                            widthM,
                        });
                    }
                }
            }

            return BridgeResponse.Json(new
            {
                category,
                totalMatches = total,
                returned = results.Count,
                note = "use the exact 'name' value with /build/place; locked prefabs need milestone progress",
                stalenessWarning = LockStalenessWarning,
                prefabs = results,
            });
        }

        private BridgeResponse PlaceBuilding(BridgeRequest request)
        {
            if (!TryGetCity(out _, out BridgeResponse error))
            {
                return error;
            }

            if (!request.Query.TryGetValue("prefab", out string prefabName) || string.IsNullOrEmpty(prefabName))
            {
                return BridgeResponse.Error(400, "provide ?prefab=<name from /prefabs>");
            }
            if (!request.TryGetFloat("x", out float x) || !request.TryGetFloat("z", out float z))
            {
                return BridgeResponse.Error(400, "provide ?x=<float>&z=<float> world coordinates");
            }
            request.TryGetFloat("rotation", out float rotationDegrees);

            if (!TryFindPrefabByName(BuildingPrefabQuery, prefabName, out Entity prefabEntity, out PrefabBase prefab)
                && !TryFindPrefabByName(TreePrefabQuery, prefabName, out prefabEntity, out prefab))
            {
                return BridgeResponse.Error(404, $"unknown building/tree prefab '{prefabName}'; search via /prefabs?category=building|tree&query=...");
            }
            if (IsLocked(prefabEntity) && !IsForced(request))
            {
                return BridgeResponse.Error(409, $"prefab '{prefab.name}' is locked (milestone not reached); pass force=true to place anyway");
            }

            float3 position = new float3(x, 0f, z);
            if (request.TryGetFloat("y", out float y))
            {
                position.y = y;
            }
            else
            {
                TerrainSystem terrain = World.GetOrCreateSystemManaged<TerrainSystem>();
                TerrainHeightData heightData = terrain.GetHeightData();
                position.y = TerrainUtils.SampleHeight(ref heightData, position);
            }
            quaternion rotation = quaternion.RotateY(math.radians(rotationDegrees));
            bool requiresShoreline = EntityManager.HasComponent<PlaceableObjectData>(prefabEntity)
                && (EntityManager.GetComponentData<PlaceableObjectData>(prefabEntity).m_Flags
                    & Game.Objects.PlacementFlags.Shoreline) != 0;
            WaterSurfaceData<SurfaceWater> waterSurfaceData = default;
            if (requiresShoreline)
            {
                WaterSystem water = World.GetOrCreateSystemManaged<WaterSystem>();
                waterSurfaceData = water.GetSurfaceData(out JobHandle waterDeps);
                waterDeps.Complete();
            }

            BridgeToolSystem tool = World.GetOrCreateSystemManaged<BridgeToolSystem>();
            float searchRadius = request.TryGetFloat("radius", out float rawRadius)
                ? math.clamp(rawRadius, 8f, 300f)
                : 0f;
            bool hasRotation = request.Query.ContainsKey("rotation");
            float baseRotation = hasRotation ? rotationDegrees : 0f;
            if (searchRadius > 0f)
            {
                // One-step find+place WITHOUT the multi-candidate tool probe
                // (the game disables a tool after a rejected preview, which
                // wedges the probe state machine). Instead we search with our
                // own heuristics (owned tile, no overlap, near a road) and then
                // commit the single best candidate through the normal placement
                // pipeline, where the game does the final validation.
                float2 center = new float2(x, z);
                const int step = 8;
                int halfSteps = math.max(1, (int)math.floor(searchRadius / step));
                var positions = new List<float3>();
                TerrainSystem terrain = World.GetOrCreateSystemManaged<TerrainSystem>();
                TerrainHeightData heightData = terrain.GetHeightData();
                for (int dz = -halfSteps; dz <= halfSteps; dz++)
                {
                    for (int dx = -halfSteps; dx <= halfSteps; dx++)
                    {
                        float cx = x + dx * step;
                        float cz = z + dz * step;
                        if (math.abs(cx - x) > searchRadius || math.abs(cz - z) > searchRadius)
                        {
                            continue;
                        }
                        var candidate = new float3(cx, 0f, cz);
                        candidate.y = TerrainUtils.SampleHeight(ref heightData, candidate);
                        positions.Add(candidate);
                    }
                }
                positions.Sort((a, b) =>
                {
                    float da = math.lengthsq(new float2(a.x, a.z) - center);
                    float db = math.lengthsq(new float2(b.x, b.z) - center);
                    return da.CompareTo(db);
                });
                bool found = false;
                string lastReason = "no candidate positions in radius";
                foreach (float3 p in positions)
                {
                    float candidateRotation = hasRotation
                        ? baseRotation
                        : AutoRotationTowardsRoad(p);
                    quaternion candidateQuaternion = quaternion.RotateY(math.radians(candidateRotation));
                    if (IsCandidateBuildable(
                            prefabEntity,
                            p,
                            candidateQuaternion,
                            requiresShoreline,
                            ref waterSurfaceData,
                            out string reason))
                    {
                        position = p;
                        rotation = candidateQuaternion;
                        found = true;
                        break;
                    }
                    lastReason = reason;
                }
                if (!found)
                {
                    return BridgeResponse.Error(404,
                        $"no valid placement found inside radius {searchRadius:F0}m around ({x:F0},{z:F0}): " +
                        lastReason + ". Try another center, or build a road to the site first.");
                }
            }
            else if (!hasRotation)
            {
                // Exact coordinates but auto-orient the building toward the
                // nearest road (front faces +Z at rotation 0 in CS2).
                rotation = quaternion.RotateY(math.radians(AutoRotationTowardsRoad(position)));
            }
            if (searchRadius <= 0f
                && !IsForced(request)
                && !IsCandidateBuildable(
                    prefabEntity,
                    position,
                    rotation,
                    requiresShoreline,
                    ref waterSurfaceData,
                    out string exactReason))
            {
                return BridgeResponse.Error(409,
                    $"cannot place '{prefab.name}' at ({x:F0},{z:F0}): {exactReason}. " +
                    "Use radius to search nearby, or build a road to the site first.");
            }

            // Resolve the connector only after radius search and rotation have
            // chosen the final placement. Otherwise a successful shifted
            // placement can receive a pipe/cable aimed from the search center.
            ResolveAutoConnect(
                prefabEntity,
                position,
                roadFrontageVerified: !IsForced(request),
                out Entity connectPrefabEntity,
                out PrefabBase connectPrefab,
                out float3 connectEnd);

            if (!tool.TryQueuePlacement(
                prefabEntity,
                prefab,
                position,
                rotation,
                request,
                connectPrefabEntity,
                connectPrefab,
                position,
                connectEnd))
            {
                return BridgeResponse.Error(409, "another build operation is in progress, retry shortly");
            }
            // Completed asynchronously by BridgeToolSystem over the next tool frames.
            return null;
        }

        private BridgeResponse FindPlacement(BridgeRequest request)
        {
            if (!TryGetCity(out _, out BridgeResponse error))
            {
                return error;
            }

            if (!request.Query.TryGetValue("prefab", out string prefabName) || string.IsNullOrEmpty(prefabName))
            {
                return BridgeResponse.Error(400, "provide ?prefab=<name from /prefabs>");
            }
            if (!request.TryGetFloat("x", out float x) || !request.TryGetFloat("z", out float z))
            {
                return BridgeResponse.Error(400, "provide ?x=<float>&z=<float> search center");
            }
            float radius = request.TryGetFloat("radius", out float rawRadius)
                ? math.clamp(rawRadius, 8f, 300f)
                : 40f;
            int maxAttempts = request.TryGetInt("attempts", out int rawAttempts)
                ? math.clamp(rawAttempts, 1, 1)
                : 1;
            request.TryGetFloat("rotation", out float rotationDegrees);

            if (!TryFindPrefabByName(BuildingPrefabQuery, prefabName, out Entity prefabEntity, out PrefabBase prefab)
                && !TryFindPrefabByName(TreePrefabQuery, prefabName, out prefabEntity, out prefab))
            {
                return BridgeResponse.Error(404, $"unknown building/tree prefab '{prefabName}'; search via /prefabs?category=building|tree&query=...");
            }
            if (IsLocked(prefabEntity) && !IsForced(request))
            {
                return BridgeResponse.Error(409, $"prefab '{prefab.name}' is locked (milestone not reached); pass force=true to search anyway");
            }

            float2 center = new float2(x, z);
            const int step = 8;
            int halfSteps = math.max(1, (int)math.floor(radius / step));
            var candidates = new List<float3>();
            for (int dz = -halfSteps; dz <= halfSteps; dz++)
            {
                for (int dx = -halfSteps; dx <= halfSteps; dx++)
                {
                    float cx = x + dx * step;
                    float cz = z + dz * step;
                    if (math.abs(cx - x) > radius || math.abs(cz - z) > radius)
                    {
                        continue;
                    }
                    candidates.Add(new float3(cx, 0f, cz));
                }
            }
            candidates.Sort((a, b) =>
            {
                float da = math.lengthsq(new float2(a.x, a.z) - center);
                float db = math.lengthsq(new float2(b.x, b.z) - center);
                return da.CompareTo(db);
            });
            if (candidates.Count > maxAttempts)
            {
                candidates.RemoveRange(maxAttempts, candidates.Count - maxAttempts);
            }

            TerrainSystem terrain = World.GetOrCreateSystemManaged<TerrainSystem>();
            TerrainHeightData heightData = terrain.GetHeightData();
            for (int i = 0; i < candidates.Count; i++)
            {
                float3 position = candidates[i];
                position.y = TerrainUtils.SampleHeight(ref heightData, position);
                candidates[i] = position;
            }

            BridgeToolSystem tool = World.GetOrCreateSystemManaged<BridgeToolSystem>();
            if (!tool.TryQueueProbe(prefabEntity, prefab, candidates, rotationDegrees, request))
            {
                return BridgeResponse.Error(409, "another build operation is in progress, retry shortly");
            }
            // Completed asynchronously by BridgeToolSystem over the next tool frames.
            return null;
        }

        private BridgeResponse BuildRoad(BridgeRequest request)
        {
            if (!TryGetCity(out _, out BridgeResponse error))
            {
                return error;
            }

            if (!request.Query.TryGetValue("prefab", out string prefabName) || string.IsNullOrEmpty(prefabName))
            {
                return BridgeResponse.Error(400, "provide ?prefab=<name from /prefabs?category=road>");
            }
            if (!request.TryGetFloat("x1", out float x1) || !request.TryGetFloat("z1", out float z1)
                || !request.TryGetFloat("x2", out float x2) || !request.TryGetFloat("z2", out float z2))
            {
                return BridgeResponse.Error(400, "provide ?x1=&z1=&x2=&z2= world coordinates for both endpoints");
            }

            float length = math.distance(new float2(x1, z1), new float2(x2, z2));
            if (length < 8f)
            {
                return BridgeResponse.Error(400, $"segment too short ({length:F1}m); minimum ~8m");
            }
            if (length > 1500f)
            {
                return BridgeResponse.Error(400, $"segment too long ({length:F0}m); split into segments of <=1500m");
            }

            if (!TryFindPrefabByName(NetPrefabQuery, prefabName, out Entity prefabEntity, out PrefabBase prefab))
            {
                return BridgeResponse.Error(404, $"unknown network prefab '{prefabName}'; search via /prefabs?category=road|net&query=...");
            }
            if (IsLocked(prefabEntity) && !IsForced(request))
            {
                return BridgeResponse.Error(409, $"prefab '{prefab.name}' is locked (milestone not reached); pass force=true to build anyway");
            }

            TerrainSystem terrain = World.GetOrCreateSystemManaged<TerrainSystem>();
            TerrainHeightData heightData = terrain.GetHeightData();
            float3 start = new float3(x1, 0f, z1);
            start.y = TerrainUtils.SampleHeight(ref heightData, start);
            float3 end = new float3(x2, 0f, z2);
            end.y = TerrainUtils.SampleHeight(ref heightData, end);

            bool hasMid = request.TryGetFloat("cx", out float cx) & request.TryGetFloat("cz", out float cz);
            float3 mid = default;
            if (hasMid)
            {
                mid = new float3(cx, 0f, cz);
                mid.y = TerrainUtils.SampleHeight(ref heightData, mid);
            }

            request.TryGetFloat("e1", out float e1);
            request.TryGetFloat("e2", out float e2);
            // Query keys are always present as strings when the agent passes them;
            // reject absurd values instead of silently clamping (models often
            // confuse e1/e2 with entity indexes ~hundreds).
            if (request.Query.ContainsKey("e1") && (e1 < -30f || e1 > 60f))
            {
                return BridgeResponse.Error(400,
                    $"e1={e1:F0} out of range; e1/e2 are elevation in meters relative to terrain (-30..60), " +
                    "not entity indexes. Omit them for ground-level roads; use ~5-20 for short bridges.");
            }
            if (request.Query.ContainsKey("e2") && (e2 < -30f || e2 > 60f))
            {
                return BridgeResponse.Error(400,
                    $"e2={e2:F0} out of range; e1/e2 are elevation in meters relative to terrain (-30..60), " +
                    "not entity indexes. Omit them for ground-level roads; use ~5-20 for short bridges.");
            }
            if (!request.Query.ContainsKey("e1") && !request.Query.ContainsKey("e2")
                && IsBuriedNetPrefab(prefab.name))
            {
                // Pipes and ground cables are underground networks: default to
                // -10m so they are actually buried instead of floating on the
                // surface.
                e1 = -10f;
                e2 = -10f;
            }
            var elevations = new float2(e1, e2);

            BridgeToolSystem tool = World.GetOrCreateSystemManaged<BridgeToolSystem>();
            if (!tool.TryQueueRoad(prefabEntity, prefab, start, end, mid, hasMid, elevations, request))
            {
                return BridgeResponse.Error(409, "another build operation is in progress, retry shortly");
            }
            return null;
        }

        private static readonly Dictionary<string, (Game.Prefabs.CompositionFlags.General general, Game.Prefabs.CompositionFlags.Side side)> kUpgradeNames =
            new Dictionary<string, (Game.Prefabs.CompositionFlags.General, Game.Prefabs.CompositionFlags.Side)>(StringComparer.OrdinalIgnoreCase)
            {
                ["grass"] = (default, Game.Prefabs.CompositionFlags.Side.PrimaryBeautification),
                ["trees"] = (default, Game.Prefabs.CompositionFlags.Side.SecondaryBeautification),
                ["wideSidewalk"] = (default, Game.Prefabs.CompositionFlags.Side.WideSidewalk),
                ["soundBarrier"] = (default, Game.Prefabs.CompositionFlags.Side.SoundBarrier),
                ["parking"] = (default, Game.Prefabs.CompositionFlags.Side.ParkingSpaces),
                ["lighting"] = (Game.Prefabs.CompositionFlags.General.Lighting, default),
                ["medianGrass"] = (Game.Prefabs.CompositionFlags.General.PrimaryMiddleBeautification, default),
                ["medianTrees"] = (Game.Prefabs.CompositionFlags.General.SecondaryMiddleBeautification, default),
            };

        private BridgeResponse HandleUpgradeRoad(BridgeRequest request)
        {
            if (!TryGetCity(out _, out BridgeResponse error))
            {
                return error;
            }
            if (!request.TryGetInt("index", out int index) || !request.TryGetInt("version", out int version))
            {
                return BridgeResponse.Error(400, "provide ?index=&version= of a road segment from /city/roads");
            }
            if (!request.Query.TryGetValue("upgrades", out string upgradesRaw) || string.IsNullOrEmpty(upgradesRaw))
            {
                return BridgeResponse.Error(400,
                    $"provide ?upgrades=<comma list>: {string.Join(", ", kUpgradeNames.Keys)}");
            }

            var entity = new Entity { Index = index, Version = version };
            if (!EntityManager.Exists(entity) || !EntityManager.HasComponent<Game.Net.Edge>(entity))
            {
                return BridgeResponse.Error(404, $"entity {index}:{version} is not an existing road segment");
            }

            string side = request.Query.TryGetValue("side", out string rawSide) ? rawSide.ToLowerInvariant() : "both";
            Game.Prefabs.CompositionFlags flags = default;
            foreach (string name in upgradesRaw.Split(','))
            {
                string trimmed = name.Trim();
                if (!kUpgradeNames.TryGetValue(trimmed, out (Game.Prefabs.CompositionFlags.General general, Game.Prefabs.CompositionFlags.Side side) mapped))
                {
                    return BridgeResponse.Error(400, $"unknown upgrade '{trimmed}'; valid: {string.Join(", ", kUpgradeNames.Keys)}");
                }
                flags.m_General |= mapped.general;
                if (side == "left" || side == "both")
                {
                    flags.m_Left |= mapped.side;
                }
                if (side == "right" || side == "both")
                {
                    flags.m_Right |= mapped.side;
                }
            }

            PrefabSystem prefabSystem = World.GetOrCreateSystemManaged<PrefabSystem>();
            string prefabName = null;
            if (EntityManager.HasComponent<PrefabRef>(entity))
            {
                PrefabBase prefab = prefabSystem.GetPrefab<PrefabBase>(EntityManager.GetComponentData<PrefabRef>(entity).m_Prefab);
                prefabName = prefab != null ? prefab.name : null;
            }

            BridgeToolSystem tool = World.GetOrCreateSystemManaged<BridgeToolSystem>();
            if (!tool.TryQueueUpgrade(entity, prefabName, flags, request))
            {
                return BridgeResponse.Error(409, "another build operation is in progress, retry shortly");
            }
            return null;
        }

        private BridgeResponse ListBuildings(BridgeRequest request)
        {
            if (!TryGetCity(out _, out BridgeResponse error))
            {
                return error;
            }

            request.Query.TryGetValue("query", out string search);
            const int hardMax = 128;
            int limit = request.TryGetInt("limit", out int rawLimit) ? math.clamp(rawLimit, 1, hardMax) : hardMax;
            bool hasCenter = request.TryGetFloat("x", out float x) & request.TryGetFloat("z", out float z);
            float radius = request.TryGetFloat("radius", out float rawRadius) ? math.max(rawRadius, 1f) : 250f;
            float2 center = new float2(x, z);

            PrefabSystem prefabSystem = World.GetOrCreateSystemManaged<PrefabSystem>();
            var found = new List<(float distance, object item)>();
            int total = 0;
            using (NativeArray<Entity> entities = PlacedBuildingQuery.ToEntityArray(Allocator.Temp))
            {
                foreach (Entity entity in entities)
                {
                    PrefabRef prefabRef = EntityManager.GetComponentData<PrefabRef>(entity);
                    PrefabBase prefab = prefabSystem.GetPrefab<PrefabBase>(prefabRef.m_Prefab);
                    string name = prefab != null ? prefab.name : "<unknown>";
                    if (!string.IsNullOrEmpty(search)
                        && name.IndexOf(search, StringComparison.OrdinalIgnoreCase) < 0)
                    {
                        continue;
                    }
                    Transform transform = EntityManager.GetComponentData<Transform>(entity);
                    if (hasCenter && math.distance(transform.m_Position.xz, center) > radius)
                    {
                        continue;
                    }
                    total++;
                    float distance = hasCenter ? math.distance(transform.m_Position.xz, center) : 0f;
                    object lotSize = null;
                    object footprintMeters = null;
                    if (EntityManager.HasComponent<BuildingData>(prefabRef.m_Prefab))
                    {
                        int2 lot = EntityManager.GetComponentData<BuildingData>(prefabRef.m_Prefab).m_LotSize;
                        lotSize = new { x = lot.x, z = lot.y };
                        footprintMeters = new
                        {
                            x = (float)Math.Round(lot.x * kFootprintCellSize, 1),
                            z = (float)Math.Round(lot.y * kFootprintCellSize, 1),
                        };
                    }
                    var item = new
                    {
                        entity = new { index = entity.Index, version = entity.Version },
                        prefab = name,
                        isSubBuilding = EntityManager.HasComponent<Game.Common.Owner>(entity),
                        position = new
                        {
                            x = transform.m_Position.x,
                            y = transform.m_Position.y,
                            z = transform.m_Position.z,
                        },
                        lotSize,
                        footprintMeters,
                        distanceM = hasCenter ? (double?)Math.Round(distance, 1) : null,
                    };
                    if (found.Count < limit)
                    {
                        found.Add((distance, item));
                    }
                    else if (hasCenter)
                    {
                        int worst = 0;
                        for (int j = 1; j < found.Count; j++)
                        {
                            if (found[j].distance > found[worst].distance)
                            {
                                worst = j;
                            }
                        }
                        if (distance < found[worst].distance)
                        {
                            found[worst] = (distance, item);
                        }
                    }
                }
            }

            if (hasCenter && found.Count > 1)
            {
                for (int i = 0; i < found.Count - 1; i++)
                {
                    for (int j = i + 1; j < found.Count; j++)
                    {
                        if (found[j].distance < found[i].distance)
                        {
                            (found[i], found[j]) = (found[j], found[i]);
                        }
                    }
                }
            }
            var results = new List<object>(found.Count);
            foreach ((_, object item) in found)
            {
                results.Add(item);
            }

            return BridgeResponse.Json(new
            {
                totalMatches = total,
                returned = results.Count,
                limit,
                truncated = total > results.Count,
                warning = total > results.Count
                    ? $"too many results: {total} buildings match, only {results.Count} returned; shrink radius / add query filter, or paginate."
                    : null,
                note = "hard max 128; sorted by distanceM when x/z given; use entity index+version with /build/demolish",
                buildings = results,
            });
        }

        private static float? NetworkWidthM(EntityManager entityManager, Entity prefabEntity)
        {
            if (!entityManager.HasComponent<NetGeometryData>(prefabEntity))
            {
                return null;
            }
            return (float?)Math.Round(
                entityManager.GetComponentData<NetGeometryData>(prefabEntity).m_DefaultWidth,
                1);
        }

        private static bool IsBuriedNetPrefab(string name)
        {
            return !string.IsNullOrEmpty(name)
                && (name.IndexOf("Pipe", StringComparison.OrdinalIgnoreCase) >= 0
                    || name.IndexOf("Ground Cable", StringComparison.OrdinalIgnoreCase) >= 0);
        }

        private BridgeResponse Demolish(BridgeRequest request)
        {
            if (!TryGetCity(out _, out BridgeResponse error))
            {
                return error;
            }

            if (!request.TryGetInt("index", out int index) || !request.TryGetInt("version", out int version))
            {
                return BridgeResponse.Error(400, "provide ?index=<int>&version=<int> from /city/buildings");
            }

            var entity = new Entity { Index = index, Version = version };
            if (!EntityManager.Exists(entity))
            {
                return BridgeResponse.Error(404, $"entity {index}:{version} does not exist (stale id?)");
            }
            bool isBuilding = EntityManager.HasComponent<Game.Buildings.Building>(entity);
            bool isNetEdge = EntityManager.HasComponent<Game.Net.Edge>(entity);
            bool isFlora = EntityManager.HasComponent<Game.Objects.Tree>(entity)
                || EntityManager.HasComponent<Game.Objects.Plant>(entity);
            bool isDistrict = EntityManager.HasComponent<Game.Areas.District>(entity);
            if (!isBuilding && !isNetEdge && !isFlora && !isDistrict)
            {
                return BridgeResponse.Error(400, "entity is not a building, road segment, tree/plant or district; refusing to delete");
            }
            if (EntityManager.HasComponent<Game.Common.Deleted>(entity))
            {
                return BridgeResponse.Error(409, "entity is already being deleted");
            }

            PrefabSystem prefabSystem = World.GetOrCreateSystemManaged<PrefabSystem>();
            string prefabName = null;
            if (EntityManager.HasComponent<PrefabRef>(entity))
            {
                PrefabBase prefab = prefabSystem.GetPrefab<PrefabBase>(EntityManager.GetComponentData<PrefabRef>(entity).m_Prefab);
                prefabName = prefab != null ? prefab.name : null;
            }

            // Deletion MUST go through the game's bulldoze pipeline. Adding a raw
            // Deleted component skips node/block/lane cleanup and corrupts state.
            BridgeToolSystem tool = World.GetOrCreateSystemManaged<BridgeToolSystem>();
            if (!tool.TryQueueDemolish(entity, prefabName, request))
            {
                return BridgeResponse.Error(409, "another build operation is in progress, retry shortly");
            }
            return null;
        }

        private static bool IsForced(BridgeRequest request)
        {
            return request.TryGetBool("force", out bool force) && force;
        }

        /// <summary>
        /// Decides whether a placed building needs an automatic utility
        /// connector (sewage pipe / water pipe / low-voltage cable) to the
        /// nearest road, and resolves the network prefab + target point.
        /// </summary>
        private void ResolveAutoConnect(
            Entity prefabEntity,
            float3 buildingPosition,
            bool roadFrontageVerified,
            out Entity netPrefabEntity,
            out PrefabBase netPrefab,
            out float3 connectEnd)
        {
            netPrefabEntity = Entity.Null;
            netPrefab = null;
            connectEnd = buildingPosition;

            string netName = null;
            if (EntityManager.HasComponent<Game.Prefabs.SewageOutletData>(prefabEntity))
            {
                netName = "Small Sewage Pipe";
            }
            else if (EntityManager.HasComponent<Game.Prefabs.WaterPumpingStationData>(prefabEntity)
                || EntityManager.HasComponent<Game.Prefabs.WaterTowerData>(prefabEntity))
            {
                // Normal placement proves the building frontage reaches a
                // road, whose built-in pipes already carry fresh water. The
                // building center is not a water socket: a redundant
                // center-to-road pipe stays disconnected and raises a warning.
                // Keep the old fallback for force=true diagnostic placements
                // that deliberately bypass frontage validation.
                if (roadFrontageVerified)
                {
                    return;
                }
                netName = "Small Water Pipe";
            }
            else if (EntityManager.HasComponent<Game.Prefabs.WindPoweredData>(prefabEntity))
            {
                netName = "Low-voltage Ground Cable";
            }
            else if (EntityManager.HasComponent<Game.Prefabs.PowerPlantData>(prefabEntity)
                || EntityManager.HasComponent<Game.Prefabs.SolarPoweredData>(prefabEntity))
            {
                // Power plants (coal, gas, solar farm, ...) output HIGH voltage;
                // wind turbines are the low-voltage producers.
                netName = "High-voltage Line";
            }
            else
            {
                return;
            }

            if (!TryFindPrefabByName(NetPrefabQuery, netName, out netPrefabEntity, out netPrefab))
            {
                netPrefabEntity = Entity.Null;
                netPrefab = null;
                return;
            }
            if (HasNetNearby(netPrefabEntity, buildingPosition, 14f))
            {
                // Already connected to this network type nearby; no stub needed.
                netPrefabEntity = Entity.Null;
                netPrefab = null;
                return;
            }
            if (!TryFindNearestRoadPoint(buildingPosition, 150f, out connectEnd))
            {
                netPrefabEntity = Entity.Null;
                netPrefab = null;
                return;
            }

            // build_road rejects segments shorter than 8m; extend the stub
            // toward the road if the building sits almost on it.
            float2 delta = connectEnd.xz - buildingPosition.xz;
            float length = math.length(delta);
            if (length < 8f)
            {
                if (length < 0.5f)
                {
                    netPrefabEntity = Entity.Null;
                    netPrefab = null;
                    return;
                }
                connectEnd = buildingPosition + new float3(math.normalizesafe(delta) * 8f, 0f);
            }
            TerrainSystem terrain = World.GetOrCreateSystemManaged<TerrainSystem>();
            TerrainHeightData heightData = terrain.GetHeightData();
            connectEnd.y = TerrainUtils.SampleHeight(ref heightData, connectEnd);
        }

        private bool HasNetNearby(Entity netPrefabEntity, float3 position, float radius)
        {
            using (NativeArray<Entity> entities = PlacedRoadQuery.ToEntityArray(Allocator.Temp))
            {
                foreach (Entity entity in entities)
                {
                    if (!EntityManager.HasComponent<PrefabRef>(entity))
                    {
                        continue;
                    }
                    PrefabRef prefabRef = EntityManager.GetComponentData<PrefabRef>(entity);
                    if (prefabRef.m_Prefab != netPrefabEntity)
                    {
                        continue;
                    }
                    Game.Net.Curve curve = EntityManager.GetComponentData<Game.Net.Curve>(entity);
                    float2 mid = (curve.m_Bezier.a.xz + curve.m_Bezier.d.xz) * 0.5f;
                    if (math.distance(mid, position.xz) <= radius)
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        private bool TryFindNearestRoadPoint(float3 from, float maxDistance, out float3 nearest)
        {
            return TryFindNearestRoadPoint(from, maxDistance, out nearest, out _);
        }

        private bool TryFindNearestRoadPoint(
            float3 from,
            float maxDistance,
            out float3 nearest,
            out float roadHalfWidth)
        {
            nearest = from;
            roadHalfWidth = 0f;
            float bestClearance = maxDistance;
            bool found = false;
            using (NativeArray<Entity> entities = PlacedRoadQuery.ToEntityArray(Allocator.Temp))
            {
                foreach (Entity entity in entities)
                {
                    if (!EntityManager.HasComponent<PrefabRef>(entity))
                    {
                        continue;
                    }
                    PrefabRef prefabRef = EntityManager.GetComponentData<PrefabRef>(entity);
                    if (!EntityManager.HasComponent<Game.Prefabs.RoadData>(prefabRef.m_Prefab))
                    {
                        continue;
                    }
                    float halfWidth = EntityManager.HasComponent<NetGeometryData>(prefabRef.m_Prefab)
                        ? EntityManager.GetComponentData<NetGeometryData>(prefabRef.m_Prefab).m_DefaultWidth * 0.5f
                        : 0f;
                    Game.Net.Curve curve = EntityManager.GetComponentData<Game.Net.Curve>(entity);
                    for (int i = 0; i <= 16; i++)
                    {
                        float3 p = BezierPoint(curve.m_Bezier, i / 16f);
                        float clearance = math.distance(p.xz, from.xz) - halfWidth;
                        if (clearance < bestClearance)
                        {
                            bestClearance = clearance;
                            nearest = p;
                            roadHalfWidth = halfWidth;
                            found = true;
                        }
                    }
                }
            }
            return found;
        }

        private static float3 BezierPoint(Bezier4x3 bezier, float t)
        {
            float3 ab = math.lerp(bezier.a, bezier.b, t);
            float3 bc = math.lerp(bezier.b, bezier.c, t);
            float3 cd = math.lerp(bezier.c, bezier.d, t);
            float3 abc = math.lerp(ab, bc, t);
            float3 bcd = math.lerp(bc, cd, t);
            return math.lerp(abc, bcd, t);
        }

        /// <summary>
        /// Buildings face +Z at rotation 0 in CS2, so point the front at the
        /// nearest road point.
        /// </summary>
        private float AutoRotationTowardsRoad(float3 position)
        {
            if (TryFindNearestRoadPoint(position, 200f, out float3 roadPoint))
            {
                float2 delta = roadPoint.xz - position.xz;
                if (math.lengthsq(delta) > 0.25f)
                {
                    return math.degrees(math.atan2(delta.x, delta.y));
                }
            }
            return 0f;
        }

        private bool IsCandidateBuildable(
            Entity prefabEntity,
            float3 position,
            quaternion rotation,
            bool requiresShoreline,
            ref WaterSurfaceData<SurfaceWater> waterSurfaceData,
            out string reason)
        {
            if (!IsOnOwnedTile(position))
            {
                reason = "outside owned map tiles (buy a tile first)";
                return false;
            }
            if (OverlapsExistingBuilding(prefabEntity, position))
            {
                reason = "overlaps an existing building";
                return false;
            }
            if (OverlapsExistingRoad(prefabEntity, position, rotation))
            {
                reason = "building footprint overlaps an existing road";
                return false;
            }
            if (EntityManager.HasComponent<BuildingData>(prefabEntity))
            {
                BuildingData building = EntityManager.GetComponentData<BuildingData>(prefabEntity);
                float3 front = position + math.forward(rotation) * (building.m_LotSize.y * 4f);
                if (!TryFindNearestRoadPoint(
                        front,
                        Game.Buildings.BuildingUtils.MAX_ROAD_CONNECTION_DISTANCE,
                        out float3 roadPoint,
                        out float roadHalfWidth))
                {
                    reason = $"building frontage is more than {Game.Buildings.BuildingUtils.MAX_ROAD_CONNECTION_DISTANCE:F1}m from a road (build a road to the site first)";
                    return false;
                }
                float centerlineDistance = math.distance(front.xz, roadPoint.xz);
                float allowed = Game.Buildings.BuildingUtils.MAX_ROAD_CONNECTION_DISTANCE + roadHalfWidth;
                if (centerlineDistance < roadHalfWidth - 2f)
                {
                    reason = "building footprint overlaps the road (move its center away from the road)";
                    return false;
                }
                if (centerlineDistance > allowed)
                {
                    reason = $"building frontage is more than {Game.Buildings.BuildingUtils.MAX_ROAD_CONNECTION_DISTANCE:F1}m from a road (build a road to the site first)";
                    return false;
                }
                if (requiresShoreline
                    && WaterUtils.SampleDepth(ref waterSurfaceData, position) > 0.05f)
                {
                    reason = "shoreline building center is in water (keep the building on dry land and only its intake/outlet side in water)";
                    return false;
                }
                if (requiresShoreline
                    && !HasWaterBehindBuilding(building, position, rotation, ref waterSurfaceData))
                {
                    reason = "the intake/outlet side does not reach surface water (move the site to the shoreline; groundWater data does not apply)";
                    return false;
                }
            }
            reason = null;
            return true;
        }

        private bool OverlapsExistingRoad(
            Entity prefabEntity,
            float3 position,
            quaternion rotation)
        {
            if (!EntityManager.HasComponent<BuildingData>(prefabEntity))
            {
                return false;
            }
            int2 lot = EntityManager.GetComponentData<BuildingData>(prefabEntity).m_LotSize;
            float halfWidth = lot.x * 4f;
            float halfDepth = lot.y * 4f;
            quaternion inverseRotation = math.inverse(rotation);
            using (NativeArray<Entity> entities = PlacedRoadQuery.ToEntityArray(Allocator.Temp))
            {
                foreach (Entity entity in entities)
                {
                    PrefabRef prefabRef = EntityManager.GetComponentData<PrefabRef>(entity);
                    if (!EntityManager.HasComponent<RoadData>(prefabRef.m_Prefab))
                    {
                        continue;
                    }
                    float roadHalfWidth = EntityManager.HasComponent<NetGeometryData>(prefabRef.m_Prefab)
                        ? EntityManager.GetComponentData<NetGeometryData>(prefabRef.m_Prefab).m_DefaultWidth * 0.5f
                        : 0f;
                    Game.Net.Curve curve = EntityManager.GetComponentData<Game.Net.Curve>(entity);
                    for (int i = 0; i <= 16; i++)
                    {
                        float3 roadPoint = BezierPoint(curve.m_Bezier, i / 16f);
                        float3 local = math.mul(inverseRotation, roadPoint - position);
                        if (math.abs(local.x) < halfWidth + roadHalfWidth - 1f
                            && math.abs(local.z) < halfDepth + roadHalfWidth - 1f)
                        {
                            return true;
                        }
                    }
                }
            }
            return false;
        }

        private static bool HasWaterBehindBuilding(
            BuildingData building,
            float3 position,
            quaternion rotation,
            ref WaterSurfaceData<SurfaceWater> surfaceData)
        {
            float3 forward = math.forward(rotation);
            float3 right = math.mul(rotation, new float3(1f, 0f, 0f));
            float backDistance = building.m_LotSize.y * 4f + 4f;
            float halfWidth = math.max(0f, building.m_LotSize.x * 3f);
            float3 backCenter = position - forward * backDistance;
            for (int i = -1; i <= 1; i++)
            {
                float3 sample = backCenter + right * (halfWidth * i);
                if (WaterUtils.SampleDepth(ref surfaceData, sample) > 0.05f)
                {
                    return true;
                }
            }
            return false;
        }

        private bool IsOnOwnedTile(float3 position)
        {
            const float kMapHalfSize = 7168f;
            const float kMapTileSize = 623.304347826f;
            int gridX = (int)Math.Floor((position.x + kMapHalfSize) / kMapTileSize);
            int gridZ = (int)Math.Floor((position.z + kMapHalfSize) / kMapTileSize);
            using (NativeArray<Entity> entities = MapTileQuery.ToEntityArray(Allocator.Temp))
            {
                foreach (Entity entity in entities)
                {
                    Game.Areas.Geometry geometry =
                        EntityManager.GetComponentData<Game.Areas.Geometry>(entity);
                    int x = (int)Math.Floor((geometry.m_CenterPosition.x + kMapHalfSize) / kMapTileSize);
                    int z = (int)Math.Floor((geometry.m_CenterPosition.z + kMapHalfSize) / kMapTileSize);
                    if (x == gridX && z == gridZ)
                    {
                        return !EntityManager.HasComponent<Game.Common.Native>(entity);
                    }
                }
            }
            return false;
        }

        private bool OverlapsExistingBuilding(Entity prefabEntity, float3 position)
        {
            float candidateRadius = BuildingRadius(prefabEntity);
            if (candidateRadius <= 0f)
            {
                return false;
            }
            using (NativeArray<Entity> entities = PlacedBuildingQuery.ToEntityArray(Allocator.Temp))
            {
                foreach (Entity entity in entities)
                {
                    Transform transform = EntityManager.GetComponentData<Transform>(entity);
                    PrefabRef prefabRef = EntityManager.GetComponentData<PrefabRef>(entity);
                    float otherRadius = BuildingRadius(prefabRef.m_Prefab);
                    if (otherRadius > 0f
                        && math.distance(transform.m_Position.xz, position.xz) < candidateRadius + otherRadius)
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        private float BuildingRadius(Entity prefabEntity)
        {
            if (!EntityManager.HasComponent<BuildingData>(prefabEntity))
            {
                return 0f;
            }
            int2 lot = EntityManager.GetComponentData<BuildingData>(prefabEntity).m_LotSize;
            return math.length(new float2(lot.x, lot.y)) * 4f;
        }

        private bool TryFindPrefabByName(EntityQuery query, string name, out Entity prefabEntity, out PrefabBase prefab)
        {
            PrefabSystem prefabSystem = World.GetOrCreateSystemManaged<PrefabSystem>();
            using (NativeArray<Entity> entities = query.ToEntityArray(Allocator.Temp))
            {
                foreach (Entity entity in entities)
                {
                    PrefabBase candidate = prefabSystem.GetPrefab<PrefabBase>(entity);
                    if (candidate != null && string.Equals(candidate.name, name, StringComparison.OrdinalIgnoreCase))
                    {
                        prefabEntity = entity;
                        prefab = candidate;
                        return true;
                    }
                }
            }
            prefabEntity = Entity.Null;
            prefab = null;
            return false;
        }
    }
}
