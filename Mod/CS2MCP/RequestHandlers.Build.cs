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
            request.Query.TryGetValue("role", out string requestedRole);
            if (!string.IsNullOrWhiteSpace(requestedRole))
            {
                requestedRole = requestedRole.Trim().ToLowerInvariant();
                if (!kPrefabRoles.Contains(requestedRole))
                {
                    return BridgeResponse.Error(BridgeErrorKind.InvalidArguments,
                        $"unknown role '{requestedRole}'; use {string.Join(", ", kPrefabRoles)}");
                }
            }
            request.Query.TryGetValue("operational_area", out string operationalAreaFilter);
            if (!string.IsNullOrWhiteSpace(operationalAreaFilter))
            {
                operationalAreaFilter = operationalAreaFilter.Trim().ToLowerInvariant();
                if (operationalAreaFilter != "any"
                    && operationalAreaFilter != "storage"
                    && operationalAreaFilter != "extractor")
                {
                    return BridgeResponse.Error(BridgeErrorKind.InvalidArguments,
                        "operational_area must be any, storage or extractor");
                }
            }
            const int hardMax = 128;
            int limit = request.TryGetInt("limit", out int rawLimit) ? math.clamp(rawLimit, 1, hardMax) : hardMax;
            bool hasCenter = request.TryGetFloat("x", out float x) & request.TryGetFloat("z", out float z);
            float radius = request.TryGetFloat("radius", out float rawRadius) ? math.max(rawRadius, 1f) : 250f;
            float2 center = new float2(x, z);
            request.Query.TryGetValue("sort", out string sort);
            sort = sort?.Trim().ToLowerInvariant();
            if (!string.IsNullOrEmpty(sort)
                && sort != "distance"
                && sort != "traffic_volume"
                && sort != "congestion")
            {
                return BridgeResponse.Error(BridgeErrorKind.InvalidArguments,
                    "sort must be distance, traffic_volume or congestion");
            }
            if (sort == "distance" && !hasCenter)
            {
                return BridgeResponse.Error(BridgeErrorKind.InvalidArguments,
                    "sort=distance requires both x and z");
            }

            PrefabSystem prefabSystem = World.GetOrCreateSystemManaged<PrefabSystem>();
            var found = new List<(float rank, float distance, object item)>();
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
                    if (!EntityManager.HasComponent<RoadData>(prefabRef.m_Prefab))
                    {
                        continue;
                    }
                    PrefabBase prefab = prefabSystem.GetPrefab<PrefabBase>(prefabRef.m_Prefab);
                    string name = prefab != null ? prefab.name : "<unknown>";
                    List<string> roles = GetPrefabRoles(prefabRef.m_Prefab);
                    if (!string.IsNullOrEmpty(search)
                        && name.IndexOf(search, StringComparison.OrdinalIgnoreCase) < 0)
                    {
                        continue;
                    }
                    if (!string.IsNullOrEmpty(requestedRole) && !roles.Contains(requestedRole))
                    {
                        continue;
                    }
                    GetBuildingOperationalCapabilities(
                        entity,
                        out bool hasStorageArea,
                        out bool hasExtractorArea,
                        out bool expandableStorageArea,
                        out bool expandableExtractorArea);
                    if ((operationalAreaFilter == "any" && !hasStorageArea && !hasExtractorArea)
                        || (operationalAreaFilter == "storage" && !hasStorageArea)
                        || (operationalAreaFilter == "extractor" && !hasExtractorArea))
                    {
                        continue;
                    }
                    total++;
                    float distance = hasCenter ? math.distance(midpoint, center) : 0f;
                    object traffic = null;
                    float volumeIndex = 0f;
                    float congestionIndex = 0f;
                    if (EntityManager.HasComponent<Game.Net.Road>(entity))
                    {
                        Game.Net.Road road = EntityManager.GetComponentData<Game.Net.Road>(entity);
                        float flowPercent = math.csum(Game.Net.NetUtils.GetTrafficFlowSpeed(road)) * 25f;
                        volumeIndex = math.csum(
                            (road.m_TrafficFlowDistance0 + road.m_TrafficFlowDistance1)
                            * 2.6666667f) * 0.25f;
                        congestionIndex = volumeIndex * math.saturate(1f - flowPercent * 0.01f);
                        int activeBottlenecks = 0;
                        if (EntityManager.HasBuffer<Game.Net.SubLane>(entity))
                        {
                            DynamicBuffer<Game.Net.SubLane> lanes =
                                EntityManager.GetBuffer<Game.Net.SubLane>(entity, isReadOnly: true);
                            foreach (Game.Net.SubLane lane in lanes)
                            {
                                if (EntityManager.HasComponent<Game.Net.Bottleneck>(lane.m_SubLane)
                                    && EntityManager.GetComponentData<Game.Net.Bottleneck>(lane.m_SubLane).m_Timer >= 20)
                                {
                                    activeBottlenecks++;
                                }
                            }
                        }
                        traffic = new
                        {
                            flowPercent = (float)Math.Round(flowPercent, 1),
                            volumeIndex = (float)Math.Round(volumeIndex, 1),
                            congestionIndex = (float)Math.Round(congestionIndex, 1),
                            activeBottlenecks,
                        };
                    }
                    float rank = sort == "traffic_volume"
                        ? -volumeIndex
                        : sort == "congestion"
                            ? -congestionIndex
                            : distance;
                    var item = new
                    {
                        entity = new { index = entity.Index, version = entity.Version },
                        prefab = name,
                        roles,
                        capabilities = new
                        {
                            operationalArea = hasStorageArea || hasExtractorArea,
                            storageArea = hasStorageArea,
                            extractorArea = hasExtractorArea,
                            expandableStorageArea,
                            expandableExtractorArea,
                        },
                        start = new { x = curve.m_Bezier.a.x, z = curve.m_Bezier.a.z },
                        end = new { x = curve.m_Bezier.d.x, z = curve.m_Bezier.d.z },
                        length = curve.m_Length,
                        widthM = NetworkWidthM(EntityManager, prefabRef.m_Prefab),
                        distanceM = hasCenter ? (double?)Math.Round(distance, 1) : null,
                        traffic,
                    };
                    if (found.Count < limit)
                    {
                        found.Add((rank, distance, item));
                    }
                    else if (hasCenter || !string.IsNullOrEmpty(sort))
                    {
                        int worst = 0;
                        for (int j = 1; j < found.Count; j++)
                        {
                            if (found[j].rank > found[worst].rank)
                            {
                                worst = j;
                            }
                        }
                        if (rank < found[worst].rank)
                        {
                            found[worst] = (rank, distance, item);
                        }
                    }
                }
            }

            if ((hasCenter || !string.IsNullOrEmpty(sort)) && found.Count > 1)
            {
                for (int i = 0; i < found.Count - 1; i++)
                {
                    for (int j = i + 1; j < found.Count; j++)
                    {
                        if (found[j].rank < found[i].rank)
                        {
                            (found[i], found[j]) = (found[j], found[i]);
                        }
                    }
                }
            }
            var results = new List<object>(found.Count);
            foreach ((_, _, object item) in found)
            {
                results.Add(item);
            }

            bool truncated = total > results.Count;
            return BridgeResponse.Json(new
            {
                totalMatches = total,
                returned = results.Count,
                limit,
                sort,
                truncated,
                warning = truncated
                    ? $"too many results: {total} road segments match, only {results.Count} returned; shrink radius / add query filter, or paginate."
                    : null,
                note = "one entry per road segment; traffic uses the game's four-period aggregate: flowPercent=relative speed, volumeIndex=native relative volume, congestionIndex=slowdown weighted by volume; hard max 128",
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
                    return BridgeResponse.Error(BridgeErrorKind.InvalidArguments, "category must be 'building', 'road', 'net' (all networks incl. pipes/power/tracks/paths) or 'tree'");
            }

            request.Query.TryGetValue("query", out string search);
            request.Query.TryGetValue("role", out string requestedRole);
            requestedRole = requestedRole?.Trim().ToLowerInvariant();
            if (!string.IsNullOrEmpty(requestedRole) && !kPrefabRoles.Contains(requestedRole))
            {
                return BridgeResponse.Error(BridgeErrorKind.InvalidArguments,
                    $"unknown prefab role '{requestedRole}'; valid: {string.Join(", ", kPrefabRoles)}");
            }
            int limit = request.TryGetInt("limit", out int rawLimit) ? math.clamp(rawLimit, 1, 200) : 50;

            PrefabSystem prefabSystem = World.GetOrCreateSystemManaged<PrefabSystem>();
            var results = new List<object>();
            int total = 0;
            using (NativeArray<Entity> entities = query.ToEntityArray(Allocator.Temp))
            {
                foreach (Entity entity in entities)
                {
                    if (category == "building" && !IsIndependentBuildingPrefab(entity))
                    {
                        continue;
                    }
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
                    List<string> roles = GetPrefabRoles(entity);
                    if (!string.IsNullOrEmpty(requestedRole) && !roles.Contains(requestedRole))
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
                            roles,
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
                role = requestedRole,
                totalMatches = total,
                returned = results.Count,
                note = "use the exact 'name' value with /build/place; locked prefabs need milestone progress",
                stalenessWarning = LockStalenessWarning,
                prefabs = results,
            });
        }

        private static readonly HashSet<string> kPrefabRoles = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "power",
            "water",
            "sewage",
            "garbage",
            "healthcare",
            "fire",
            "police",
            "education",
            "transport",
            "post",
            "telecom",
            "specialized-industry",
        };

        private List<string> GetPrefabRoles(Entity prefab)
        {
            var roles = new List<string>();
            AddPrefabRole<PowerPlantData>(prefab, "power", roles);
            AddPrefabRole<PowerLineData>(prefab, "power", roles);
            AddPrefabRole<WaterPumpingStationData>(prefab, "water", roles);
            AddPrefabRole<WaterTowerData>(prefab, "water", roles);
            AddPrefabRole<WaterPipeConnectionData>(prefab, "water", roles);
            AddPrefabRole<SewageOutletData>(prefab, "sewage", roles);
            AddPrefabRole<GarbageFacilityData>(prefab, "garbage", roles);
            AddPrefabRole<HospitalData>(prefab, "healthcare", roles);
            AddPrefabRole<FireStationData>(prefab, "fire", roles);
            AddPrefabRole<PoliceStationData>(prefab, "police", roles);
            AddPrefabRole<SchoolData>(prefab, "education", roles);
            AddPrefabRole<TransportDepotData>(prefab, "transport", roles);
            AddPrefabRole<TransportStationData>(prefab, "transport", roles);
            AddPrefabRole<PostFacilityData>(prefab, "post", roles);
            AddPrefabRole<TelecomFacilityData>(prefab, "telecom", roles);
            AddPrefabRole<ExtractorFacilityData>(prefab, "specialized-industry", roles);
            return roles;
        }

        private void AddPrefabRole<T>(Entity prefab, string role, List<string> roles)
            where T : unmanaged, IComponentData
        {
            if (EntityManager.HasComponent<T>(prefab) && !roles.Contains(role))
            {
                roles.Add(role);
            }
        }

        private BridgeResponse PlaceBuilding(BridgeRequest request)
        {
            if (!TryGetCity(out _, out BridgeResponse error))
            {
                return error;
            }

            if (!request.Query.TryGetValue("prefab", out string prefabName) || string.IsNullOrEmpty(prefabName))
            {
                return BridgeResponse.Error(BridgeErrorKind.InvalidArguments, "provide ?prefab=<name from /prefabs>");
            }
            if (!request.TryGetFloat("x", out float x) || !request.TryGetFloat("z", out float z))
            {
                return BridgeResponse.Error(BridgeErrorKind.InvalidArguments, "provide ?x=<float>&z=<float> world coordinates");
            }
            request.TryGetFloat("rotation", out float rotationDegrees);

            if (!TryFindStandaloneObjectPrefab(
                    prefabName,
                    out Entity prefabEntity,
                    out PrefabBase prefab,
                    out BridgeResponse prefabError))
            {
                return prefabError;
            }
            if (IsLocked(prefabEntity))
            {
                return BridgeResponse.Error(BridgeErrorKind.Conflict, $"prefab '{prefab.name}' is locked (milestone not reached)");
            }

            PlacementCapabilities capabilities = GetPlacementCapabilities(prefabEntity);
            bool hasY = request.TryGetFloat("y", out float y);
            float3 requestedPosition = new float3(x, hasY ? y : 0f, z);
            bool hasRotation = request.Query.ContainsKey("rotation");
            TerrainSystem terrain = World.GetOrCreateSystemManaged<TerrainSystem>();
            TerrainHeightData heightData = terrain.GetHeightData();
            WaterSurfaceData<SurfaceWater> waterSurfaceData = default;
            if (capabilities.RequiresShoreline)
            {
                WaterSystem water = World.GetOrCreateSystemManaged<WaterSystem>();
                waterSurfaceData = water.GetSurfaceData(out JobHandle waterDeps);
                waterDeps.Complete();
            }

            float searchRadius = request.TryGetFloat("radius", out float rawRadius)
                ? math.clamp(rawRadius, 8f, 300f)
                : 0f;
            PlacementPose pose = default;
            if (searchRadius > 0f)
            {
                bool found = false;
                string lastReason = "no candidate positions in radius";
                foreach (PlacementSeed seed in CreatePlacementSearchSeeds(
                    capabilities,
                    requestedPosition.xz,
                    searchRadius,
                    8f,
                    ref waterSurfaceData))
                {
                    bool candidateHasRotation = hasRotation || seed.HasExplicitRotation;
                    float candidateRotation = hasRotation
                        ? rotationDegrees
                        : seed.RotationDegrees;
                    if (!TryResolvePlacement(
                            capabilities,
                            seed.Position,
                            hasExplicitY: false,
                            candidateHasRotation,
                            candidateRotation,
                            ref heightData,
                            ref waterSurfaceData,
                            out PlacementPose candidate,
                            out string resolveReason))
                    {
                        lastReason = resolveReason;
                        continue;
                    }
                    if (!IsCandidateBuildable(
                            capabilities,
                            candidate,
                            out _,
                            out string buildableReason))
                    {
                        lastReason = buildableReason;
                        continue;
                    }
                    pose = candidate;
                    found = true;
                    break;
                }
                if (!found)
                {
                    return BridgeResponse.Error(BridgeErrorKind.NotFound,
                        $"no valid placement found inside radius {searchRadius:F0}m around ({x:F0},{z:F0}): " +
                        lastReason + ". " + PlacementRetryHint(
                            capabilities,
                            searchRadius,
                            hasRotation));
                }
            }
            else if (!TryResolvePlacement(
                    capabilities,
                    requestedPosition,
                    hasY,
                    hasRotation,
                    rotationDegrees,
                    ref heightData,
                    ref waterSurfaceData,
                    out pose,
                    out string resolveReason))
            {
                return BridgeResponse.Error(BridgeErrorKind.Conflict,
                    $"cannot resolve placement for '{prefab.name}' near ({x:F0},{z:F0}): {resolveReason}. " +
                    PlacementRetryHint(capabilities, searchRadius, hasRotation));
            }
            if (searchRadius <= 0f
                && !IsCandidateBuildable(
                    capabilities,
                    pose,
                    out _,
                    out string exactReason))
            {
                return BridgeResponse.Error(BridgeErrorKind.Conflict,
                    $"cannot place '{prefab.name}' at ({x:F0},{z:F0}): {exactReason}. " +
                    PlacementRetryHint(capabilities, searchRadius, hasRotation));
            }

            // Resolve the connector only after radius search and rotation have
            // chosen the final placement. Otherwise a successful shifted
            // placement can receive a pipe/cable aimed from the search center.
            if (!TryResolveAutoConnect(
                capabilities,
                pose.Position,
                pose.Rotation,
                out Entity connectPrefabEntity,
                out PrefabBase connectPrefab,
                out float3 connectStart,
                out float3 connectEnd,
                out string connectReason))
            {
                return BridgeResponse.Error(BridgeErrorKind.Conflict, connectReason);
            }

            BridgeToolSystem tool = World.GetOrCreateSystemManaged<BridgeToolSystem>();
            if (!tool.TryQueuePlacement(
                prefabEntity,
                prefab,
                pose.Position,
                pose.Rotation,
                request,
                connectPrefabEntity,
                connectPrefab,
                connectStart,
                connectEnd))
            {
                return BridgeResponse.Error(BridgeErrorKind.Conflict, "another build operation is in progress, retry shortly");
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
                return BridgeResponse.Error(BridgeErrorKind.InvalidArguments, "provide ?prefab=<name from /prefabs>");
            }
            if (!request.TryGetFloat("x", out float x) || !request.TryGetFloat("z", out float z))
            {
                return BridgeResponse.Error(BridgeErrorKind.InvalidArguments, "provide ?x=<float>&z=<float> search center");
            }
            float radius = request.TryGetFloat("radius", out float rawRadius)
                ? math.clamp(rawRadius, 8f, 300f)
                : 40f;
            int maxAttempts = request.TryGetInt("attempts", out int rawAttempts)
                ? math.clamp(rawAttempts, 1, 1)
                : 1;
            request.TryGetFloat("rotation", out float rotationDegrees);

            if (!TryFindStandaloneObjectPrefab(
                    prefabName,
                    out Entity prefabEntity,
                    out PrefabBase prefab,
                    out BridgeResponse prefabError))
            {
                return prefabError;
            }
            if (IsLocked(prefabEntity))
            {
                return BridgeResponse.Error(BridgeErrorKind.Conflict, $"prefab '{prefab.name}' is locked (milestone not reached)");
            }

            PlacementCapabilities capabilities = GetPlacementCapabilities(prefabEntity);
            TerrainSystem terrain = World.GetOrCreateSystemManaged<TerrainSystem>();
            TerrainHeightData heightData = terrain.GetHeightData();
            WaterSurfaceData<SurfaceWater> waterSurfaceData = default;
            if (capabilities.RequiresShoreline)
            {
                WaterSystem water = World.GetOrCreateSystemManaged<WaterSystem>();
                waterSurfaceData = water.GetSurfaceData(out JobHandle waterDeps);
                waterDeps.Complete();
            }
            bool hasRotation = request.Query.ContainsKey("rotation");
            var candidates = new List<float3>();
            float resolvedRotation = rotationDegrees;
            string lastReason = "no candidate positions in radius";
            foreach (PlacementSeed seed in CreatePlacementSearchSeeds(
                capabilities,
                new float2(x, z),
                radius,
                8f,
                ref waterSurfaceData))
            {
                bool candidateHasRotation = hasRotation || seed.HasExplicitRotation;
                float candidateRotation = hasRotation
                    ? rotationDegrees
                    : seed.RotationDegrees;
                if (!TryResolvePlacement(
                        capabilities,
                        seed.Position,
                        hasExplicitY: false,
                        candidateHasRotation,
                        candidateRotation,
                        ref heightData,
                        ref waterSurfaceData,
                        out PlacementPose candidate,
                        out lastReason))
                {
                    continue;
                }
                if (!IsCandidateBuildable(capabilities, candidate, out _, out lastReason))
                {
                    continue;
                }
                candidates.Add(candidate.Position);
                resolvedRotation = candidate.RotationDegrees;
                if (candidates.Count >= maxAttempts)
                {
                    break;
                }
            }
            if (candidates.Count == 0)
            {
                return BridgeResponse.Error(BridgeErrorKind.NotFound,
                    $"no placement candidate could be resolved within {radius:F0}m: {lastReason}. " +
                    PlacementRetryHint(capabilities, radius, hasRotation));
            }

            BridgeToolSystem tool = World.GetOrCreateSystemManaged<BridgeToolSystem>();
            if (!tool.TryQueueProbe(prefabEntity, prefab, candidates, resolvedRotation, request))
            {
                return BridgeResponse.Error(BridgeErrorKind.Conflict, "another build operation is in progress, retry shortly");
            }
            // Completed asynchronously by BridgeToolSystem over the next tool frames.
            return null;
        }

        private sealed class InfrastructureCandidate
        {
            public Entity PrefabEntity;
            public PrefabBase Prefab;
            public float3 Position;
            public float RotationDegrees;
            public uint ConstructionCost;
            public float DistanceFromCenter;
            public float RoadClearance;
            public int GeneratedCandidates;
            public int PreflightRejected;
        }

        private struct PlacementSeed
        {
            public float3 Position;
            public bool HasExplicitRotation;
            public float RotationDegrees;
        }

        /// <summary>
        /// Resolves one typed, unlocked service prefab and one valid site. The
        /// caller supplies gameplay intent; prefab flags select shoreline,
        /// road-side or free placement search inside this module.
        /// One final candidate is sent through the native preview pipeline so a
        /// rejected preview cannot wedge a multi-candidate tool transaction.
        /// </summary>
        private BridgeResponse FindInfrastructureCandidate(BridgeRequest request)
        {
            if (!TryGetCity(out _, out BridgeResponse error))
            {
                return error;
            }
            if (!request.Query.TryGetValue("role", out string role)
                || string.IsNullOrWhiteSpace(role))
            {
                return BridgeResponse.Error(BridgeErrorKind.InvalidArguments,
                    "provide ?role=power|water|sewage|garbage|healthcare|fire|police|education|transport|post|telecom");
            }
            role = role.Trim().ToLowerInvariant();
            if (!kInfrastructureCandidateRoles.Contains(role))
            {
                return BridgeResponse.Error(BridgeErrorKind.InvalidArguments,
                    $"role '{role}' is not supported by infrastructure candidate planning; valid: {string.Join(", ", kInfrastructureCandidateRoles)}");
            }

            bool hasX = request.TryGetFloat("x", out float x);
            bool hasZ = request.TryGetFloat("z", out float z);
            if (hasX != hasZ)
            {
                return BridgeResponse.Error(BridgeErrorKind.InvalidArguments, "provide both x and z, or omit both to search around owned tiles");
            }
            float2 center = hasX
                ? new float2(x, z)
                : GetOwnedTileCenter();
            float radius = request.TryGetFloat("radius", out float rawRadius)
                ? math.clamp(rawRadius, 64f, 2500f)
                : 900f;

            var prefabs = new List<(
                Entity entity,
                PrefabBase prefab,
                uint cost,
                PlacementCapabilities capabilities)>();
            PrefabSystem prefabSystem = World.GetOrCreateSystemManaged<PrefabSystem>();
            using (NativeArray<Entity> entities = BuildingPrefabQuery.ToEntityArray(Allocator.Temp))
            {
                foreach (Entity entity in entities)
                {
                    if (!IsIndependentBuildingPrefab(entity)
                        || IsLocked(entity)
                        || !GetPrefabRoles(entity).Contains(role))
                    {
                        continue;
                    }
                    PrefabBase prefab = prefabSystem.GetPrefab<PrefabBase>(entity);
                    if (prefab == null || !EntityManager.HasComponent<PlaceableObjectData>(entity))
                    {
                        continue;
                    }
                    PlaceableObjectData placeable = EntityManager.GetComponentData<PlaceableObjectData>(entity);
                    prefabs.Add((
                        entity,
                        prefab,
                        placeable.m_ConstructionCost,
                        GetPlacementCapabilities(entity)));
                }
            }
            if (prefabs.Count == 0)
            {
                return BridgeResponse.Error(BridgeErrorKind.NotFound,
                    $"no unlocked placeable building prefab has typed role '{role}'");
            }
            prefabs.Sort((a, b) =>
            {
                int byCost = a.cost.CompareTo(b.cost);
                return byCost != 0
                    ? byCost
                    : string.Compare(a.prefab.name, b.prefab.name, StringComparison.Ordinal);
            });

            bool needsWater = prefabs.Exists(item => item.capabilities.RequiresShoreline);
            WaterSurfaceData<SurfaceWater> waterSurfaceData = default;
            if (needsWater)
            {
                WaterSystem water = World.GetOrCreateSystemManaged<WaterSystem>();
                waterSurfaceData = water.GetSurfaceData(out JobHandle waterDependencies);
                waterDependencies.Complete();
            }
            TerrainSystem terrain = World.GetOrCreateSystemManaged<TerrainSystem>();
            TerrainHeightData heightData = terrain.GetHeightData();
            var candidates = new List<InfrastructureCandidate>();
            foreach ((
                Entity entity,
                PrefabBase prefab,
                uint cost,
                PlacementCapabilities capabilities) in prefabs)
            {
                if (TryFindInfrastructureSite(
                        prefab,
                        cost,
                        capabilities,
                        center,
                        radius,
                        ref heightData,
                        ref waterSurfaceData,
                        out InfrastructureCandidate candidate))
                {
                    candidates.Add(candidate);
                }
            }
            if (candidates.Count == 0)
            {
                return BridgeResponse.Error(BridgeErrorKind.NotFound,
                    $"no valid '{role}' site was found within {radius:F0}m of ({center.x:F0},{center.y:F0}); " +
                    "checked prefab shoreline, road-access and utility-connection requirements");
            }
            candidates.Sort((a, b) =>
            {
                int byCost = a.ConstructionCost.CompareTo(b.ConstructionCost);
                if (byCost != 0)
                {
                    return byCost;
                }
                int byDistance = a.DistanceFromCenter.CompareTo(b.DistanceFromCenter);
                if (byDistance != 0)
                {
                    return byDistance;
                }
                int byName = string.Compare(a.Prefab.name, b.Prefab.name, StringComparison.Ordinal);
                if (byName != 0)
                {
                    return byName;
                }
                int byX = a.Position.x.CompareTo(b.Position.x);
                return byX != 0 ? byX : a.Position.z.CompareTo(b.Position.z);
            });
            InfrastructureCandidate selected = candidates[0];
            BridgeToolSystem tool = World.GetOrCreateSystemManaged<BridgeToolSystem>();
            if (!tool.TryQueueInfrastructureCandidate(
                    selected.PrefabEntity,
                    selected.Prefab,
                    new BridgeToolSystem.InfrastructureCandidatePlan
                    {
                        Position = selected.Position,
                        RotationDegrees = selected.RotationDegrees,
                        Role = role,
                        GeneratedCandidates = selected.GeneratedCandidates,
                        PreflightRejected = selected.PreflightRejected,
                        ConstructionCost = selected.ConstructionCost,
                        DistanceFromCenter = selected.DistanceFromCenter,
                        RoadClearance = selected.RoadClearance,
                    },
                    request))
            {
                return BridgeResponse.Error(BridgeErrorKind.Conflict, "another build operation is in progress, retry shortly");
            }
            return null;
        }

        private bool TryFindInfrastructureSite(
            PrefabBase prefab,
            uint constructionCost,
            PlacementCapabilities capabilities,
            float2 center,
            float radius,
            ref TerrainHeightData heightData,
            ref WaterSurfaceData<SurfaceWater> waterSurfaceData,
            out InfrastructureCandidate candidate)
        {
            List<PlacementSeed> seeds = CreatePlacementSearchSeeds(
                capabilities,
                center,
                radius,
                32f,
                ref waterSurfaceData);
            int rejected = 0;
            foreach (PlacementSeed seed in seeds)
            {
                if (!TryResolvePlacement(
                        capabilities,
                        seed.Position,
                        hasExplicitY: false,
                        seed.HasExplicitRotation,
                        seed.RotationDegrees,
                        ref heightData,
                        ref waterSurfaceData,
                        out PlacementPose pose,
                        out _)
                    || math.distance(pose.Position.xz, center) > radius
                    || !IsCandidateBuildable(
                        capabilities,
                        pose,
                        out float roadClearance,
                        out _))
                {
                    rejected++;
                    continue;
                }
                candidate = new InfrastructureCandidate
                {
                    PrefabEntity = capabilities.PrefabEntity,
                    Prefab = prefab,
                    Position = pose.Position,
                    RotationDegrees = pose.RotationDegrees,
                    ConstructionCost = constructionCost,
                    DistanceFromCenter = math.distance(pose.Position.xz, center),
                    RoadClearance = roadClearance,
                    GeneratedCandidates = seeds.Count,
                    PreflightRejected = rejected,
                };
                return true;
            }
            candidate = null;
            return false;
        }

        private List<PlacementSeed> CreatePlacementSearchSeeds(
            PlacementCapabilities capabilities,
            float2 center,
            float radius,
            float gridStep,
            ref WaterSurfaceData<SurfaceWater> waterSurfaceData)
        {
            if (capabilities.RequiresShoreline)
            {
                return CreateShorelineSearchSeeds(center, radius, ref waterSurfaceData);
            }
            if (capabilities.RequiresRoad)
            {
                return CreateRoadsideSearchSeeds(capabilities.Building, center, radius);
            }

            var result = new List<PlacementSeed>();
            foreach (float3 position in CreateGridSearchSeeds(center, radius, gridStep))
            {
                result.Add(new PlacementSeed { Position = position });
            }
            return result;
        }

        private static List<PlacementSeed> CreateShorelineSearchSeeds(
            float2 center,
            float radius,
            ref WaterSurfaceData<SurfaceWater> waterSurfaceData)
        {
            float3 minWorld = new float3(center.x - radius, 0f, center.y - radius);
            float3 maxWorld = new float3(center.x + radius, 0f, center.y + radius);
            int2 min = (int2)math.floor(
                WaterUtils.ToSurfaceSpace(ref waterSurfaceData, minWorld).xz);
            int2 max = (int2)math.ceil(
                WaterUtils.ToSurfaceSpace(ref waterSurfaceData, maxWorld).xz);
            min = math.max(min, default(int2));
            max = math.min(max, waterSurfaceData.resolution.xz - 1);

            var result = new List<PlacementSeed>();
            var seen = new HashSet<long>();
            float radiusSquared = radius * radius;
            for (int z = min.y; z <= max.y; z++)
            {
                for (int x = min.x; x <= max.x; x++)
                {
                    float3 current = WaterUtils.GetWorldPosition(
                        ref waterSurfaceData,
                        new int2(x, z));
                    if (x < max.x)
                    {
                        AddShorelineTransition(
                            current,
                            WaterUtils.GetWorldPosition(
                                ref waterSurfaceData,
                                new int2(x + 1, z)),
                            center,
                            radiusSquared,
                            seen,
                            result);
                    }
                    if (z < max.y)
                    {
                        AddShorelineTransition(
                            current,
                            WaterUtils.GetWorldPosition(
                                ref waterSurfaceData,
                                new int2(x, z + 1)),
                            center,
                            radiusSquared,
                            seen,
                            result);
                    }
                }
            }
            result.Sort((a, b) =>
                math.lengthsq(a.Position.xz - center)
                    .CompareTo(math.lengthsq(b.Position.xz - center)));
            return result;
        }

        private static void AddShorelineTransition(
            float3 first,
            float3 second,
            float2 center,
            float radiusSquared,
            HashSet<long> seen,
            List<PlacementSeed> result)
        {
            bool firstWet = first.y > 0.2f;
            bool secondWet = second.y > 0.2f;
            if (firstWet == secondWet)
            {
                return;
            }
            float3 position = (first + second) * 0.5f;
            if (math.lengthsq(position.xz - center) > radiusSquared)
            {
                return;
            }
            int keyX = (int)math.round(position.x * 0.25f);
            int keyZ = (int)math.round(position.z * 0.25f);
            long key = ((long)keyX << 32) | (uint)keyZ;
            if (seen.Add(key))
            {
                result.Add(new PlacementSeed { Position = position });
            }
        }

        private List<PlacementSeed> CreateRoadsideSearchSeeds(
            BuildingData building,
            float2 center,
            float radius)
        {
            var result = new List<PlacementSeed>();
            using (NativeArray<Entity> roads = PlacedRoadQuery.ToEntityArray(Allocator.Temp))
            {
                foreach (Entity road in roads)
                {
                    PrefabRef roadPrefab = EntityManager.GetComponentData<PrefabRef>(road);
                    if (!EntityManager.HasComponent<RoadData>(roadPrefab.m_Prefab))
                    {
                        continue;
                    }
                    float roadHalfWidth = EntityManager.HasComponent<NetGeometryData>(roadPrefab.m_Prefab)
                        ? EntityManager.GetComponentData<NetGeometryData>(roadPrefab.m_Prefab).m_DefaultWidth * 0.5f
                        : 4f;
                    Game.Net.Curve curve = EntityManager.GetComponentData<Game.Net.Curve>(road);
                    int samples = math.clamp((int)math.ceil(curve.m_Length / 32f), 1, 32);
                    for (int sampleIndex = 0; sampleIndex < samples; sampleIndex++)
                    {
                        float t = (sampleIndex + 0.5f) / samples;
                        float3 roadPoint = BezierPoint(curve.m_Bezier, t);
                        float3 before = BezierPoint(curve.m_Bezier, math.max(0f, t - 0.02f));
                        float3 after = BezierPoint(curve.m_Bezier, math.min(1f, t + 0.02f));
                        float2 tangent = math.normalizesafe(after.xz - before.xz);
                        if (math.lengthsq(tangent) < 0.5f)
                        {
                            continue;
                        }
                        float2 normal = new float2(-tangent.y, tangent.x);
                        float offset = roadHalfWidth + building.m_LotSize.y * 4f + 2f;
                        for (int side = -1; side <= 1; side += 2)
                        {
                            float2 outward = normal * side;
                            float2 position = roadPoint.xz + outward * offset;
                            if (math.distance(position, center) > radius)
                            {
                                continue;
                            }
                            float2 forward = -outward;
                            result.Add(new PlacementSeed
                            {
                                Position = new float3(position.x, 0f, position.y),
                                HasExplicitRotation = true,
                                RotationDegrees = math.degrees(math.atan2(forward.x, forward.y)),
                            });
                        }
                    }
                }
            }
            result.Sort((a, b) =>
                math.lengthsq(a.Position.xz - center)
                    .CompareTo(math.lengthsq(b.Position.xz - center)));
            return result;
        }

        private static readonly HashSet<string> kInfrastructureCandidateRoles =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "power", "water", "sewage", "garbage", "healthcare", "fire",
                "police", "education", "transport", "post", "telecom",
            };

        private float2 GetOwnedTileCenter()
        {
            float2 sum = float2.zero;
            int count = 0;
            using (NativeArray<Entity> entities = MapTileQuery.ToEntityArray(Allocator.Temp))
            {
                foreach (Entity entity in entities)
                {
                    if (EntityManager.HasComponent<Game.Common.Native>(entity))
                    {
                        continue;
                    }
                    sum += EntityManager.GetComponentData<Game.Areas.Geometry>(entity).m_CenterPosition.xz;
                    count++;
                }
            }
            return count > 0 ? sum / count : float2.zero;
        }

        private BridgeResponse BuildRoad(BridgeRequest request)
        {
            if (!TryGetCity(out _, out BridgeResponse error))
            {
                return error;
            }

            if (!request.Query.TryGetValue("prefab", out string prefabName) || string.IsNullOrEmpty(prefabName))
            {
                return BridgeResponse.Error(BridgeErrorKind.InvalidArguments, "provide ?prefab=<name from /prefabs?category=road>");
            }
            if (!request.TryGetFloat("x1", out float x1) || !request.TryGetFloat("z1", out float z1)
                || !request.TryGetFloat("x2", out float x2) || !request.TryGetFloat("z2", out float z2))
            {
                return BridgeResponse.Error(BridgeErrorKind.InvalidArguments, "provide ?x1=&z1=&x2=&z2= world coordinates for both endpoints");
            }

            float length = math.distance(new float2(x1, z1), new float2(x2, z2));
            if (length < 8f)
            {
                return BridgeResponse.Error(BridgeErrorKind.InvalidArguments, $"segment too short ({length:F1}m); minimum ~8m");
            }
            if (length > 1500f)
            {
                return BridgeResponse.Error(BridgeErrorKind.InvalidArguments, $"segment too long ({length:F0}m); split into segments of <=1500m");
            }

            if (!TryFindPrefabByName(NetPrefabQuery, prefabName, out Entity prefabEntity, out PrefabBase prefab))
            {
                return BridgeResponse.Error(BridgeErrorKind.NotFound, $"unknown network prefab '{prefabName}'; search via /prefabs?category=road|net&query=...");
            }
            if (IsLocked(prefabEntity))
            {
                return BridgeResponse.Error(BridgeErrorKind.Conflict, $"prefab '{prefab.name}' is locked (milestone not reached)");
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
                return BridgeResponse.Error(BridgeErrorKind.InvalidArguments,
                    $"e1={e1:F0} out of range; e1/e2 are elevation in meters relative to terrain (-30..60), " +
                    "not entity indexes. Omit them for ground-level roads; use ~5-20 for short bridges.");
            }
            if (request.Query.ContainsKey("e2") && (e2 < -30f || e2 > 60f))
            {
                return BridgeResponse.Error(BridgeErrorKind.InvalidArguments,
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
                return BridgeResponse.Error(BridgeErrorKind.Conflict, "another build operation is in progress, retry shortly");
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

        private BridgeResponse SetRoadFeatures(BridgeRequest request)
        {
            if (!TryGetCity(out _, out BridgeResponse error))
            {
                return error;
            }
            if (!request.TryGetInt("index", out int index) || !request.TryGetInt("version", out int version))
            {
                return BridgeResponse.Error(BridgeErrorKind.InvalidArguments, "provide ?index=&version= of a road segment from /city/roads");
            }
            if (!request.Query.TryGetValue("upgrades", out string upgradesRaw) || string.IsNullOrEmpty(upgradesRaw))
            {
                return BridgeResponse.Error(BridgeErrorKind.InvalidArguments,
                    $"provide ?upgrades=<comma list>: {string.Join(", ", kUpgradeNames.Keys)}");
            }

            if (!TryResolveExistingEntity(index, version, out Entity entity)
                || !EntityManager.HasComponent<Game.Net.Edge>(entity))
            {
                return BridgeResponse.Error(BridgeErrorKind.NotFound, $"entity {index}:{version} is not an existing road segment");
            }

            string side = request.Query.TryGetValue("side", out string rawSide) ? rawSide.ToLowerInvariant() : "both";
            if (side != "left" && side != "right" && side != "both")
            {
                return BridgeResponse.Error(BridgeErrorKind.InvalidArguments, "side must be 'left', 'right' or 'both'");
            }
            Game.Prefabs.CompositionFlags flags = default;
            foreach (string name in upgradesRaw.Split(','))
            {
                string trimmed = name.Trim();
                if (!kUpgradeNames.TryGetValue(trimmed, out (Game.Prefabs.CompositionFlags.General general, Game.Prefabs.CompositionFlags.Side side) mapped))
                {
                    return BridgeResponse.Error(BridgeErrorKind.InvalidArguments, $"unknown road feature '{trimmed}'; valid: {string.Join(", ", kUpgradeNames.Keys)}");
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
                return BridgeResponse.Error(BridgeErrorKind.Conflict, "another build operation is in progress, retry shortly");
            }
            return null;
        }

        private BridgeResponse ReplaceRoadType(BridgeRequest request)
        {
            if (!TryGetCity(out _, out BridgeResponse error))
            {
                return error;
            }
            if (!request.TryGetInt("index", out int index)
                || !request.TryGetInt("version", out int version))
            {
                return BridgeResponse.Error(BridgeErrorKind.InvalidArguments,
                    "provide ?index=&version= of a standalone road segment from /city/roads");
            }
            if (!request.Query.TryGetValue("prefab", out string prefabName)
                || string.IsNullOrWhiteSpace(prefabName))
            {
                return BridgeResponse.Error(BridgeErrorKind.InvalidArguments,
                    "provide ?prefab=<exact road prefab name from find_prefabs(category=road)>");
            }

            if (!TryResolveExistingEntity(index, version, out Entity target)
                || !EntityManager.HasComponent<Game.Net.Edge>(target)
                || !EntityManager.HasComponent<Game.Net.Curve>(target)
                || !EntityManager.HasComponent<PrefabRef>(target))
            {
                return BridgeResponse.Error(BridgeErrorKind.NotFound,
                    $"entity {index}:{version} is not an existing road edge");
            }
            Entity oldPrefabEntity = EntityManager.GetComponentData<PrefabRef>(target).m_Prefab;
            if (!EntityManager.HasComponent<RoadData>(oldPrefabEntity))
            {
                return BridgeResponse.Error(BridgeErrorKind.Conflict, "target edge is not a road");
            }
            if (EntityManager.HasComponent<Game.Common.Owner>(target)
                || EntityManager.HasComponent<Game.Net.Fixed>(target))
            {
                return BridgeResponse.Error(BridgeErrorKind.Conflict,
                    "v0 replacement only accepts ownerless, non-fixed road edges");
            }

            Game.Net.Edge edge = EntityManager.GetComponentData<Game.Net.Edge>(target);
            if (!IsStandaloneRoadEndpoint(edge.m_Start) || !IsStandaloneRoadEndpoint(edge.m_End))
            {
                return BridgeResponse.Error(BridgeErrorKind.Conflict,
                    "v0 replacement only accepts a standalone edge whose endpoints each connect to exactly one edge; intersections and chain segments are not yet supported");
            }

            if (!TryFindPrefabByName(
                    RoadPrefabQuery,
                    prefabName,
                    out Entity newPrefabEntity,
                    out PrefabBase newPrefab))
            {
                return BridgeResponse.Error(BridgeErrorKind.NotFound,
                    $"unknown road prefab '{prefabName}'; search via find_prefabs(category=road)");
            }
            if (newPrefabEntity == oldPrefabEntity)
            {
                return BridgeResponse.Error(BridgeErrorKind.Conflict,
                    $"road already uses prefab '{newPrefab.name}'");
            }
            if (IsLocked(newPrefabEntity))
            {
                return BridgeResponse.Error(BridgeErrorKind.Conflict,
                    $"road prefab '{newPrefab.name}' is locked (milestone not reached)");
            }
            NetInitializeSystem netInitialize = World.GetOrCreateSystemManaged<NetInitializeSystem>();
            NetData newNetData = EntityManager.GetComponentData<NetData>(newPrefabEntity);
            if (!netInitialize.CanReplace(newNetData, inGame: true))
            {
                return BridgeResponse.Error(BridgeErrorKind.Conflict,
                    $"the game marks road prefab '{newPrefab.name}' as not replaceable in game mode");
            }

            PrefabSystem prefabSystem = World.GetOrCreateSystemManaged<PrefabSystem>();
            PrefabBase oldPrefab = prefabSystem.GetPrefab<PrefabBase>(oldPrefabEntity);
            BridgeToolSystem tool = World.GetOrCreateSystemManaged<BridgeToolSystem>();
            if (!tool.TryQueueRoadReplacement(
                    target,
                    oldPrefab != null ? oldPrefab.name : null,
                    newPrefabEntity,
                    newPrefab,
                    request))
            {
                return BridgeResponse.Error(BridgeErrorKind.Conflict,
                    "another build operation is in progress, retry shortly");
            }
            return null;
        }

        private bool IsStandaloneRoadEndpoint(Entity node)
        {
            return node != Entity.Null
                && EntityManager.Exists(node)
                && !EntityManager.HasComponent<Game.Objects.OutsideConnection>(node)
                && EntityManager.HasBuffer<Game.Net.ConnectedEdge>(node)
                && EntityManager.GetBuffer<Game.Net.ConnectedEdge>(node, isReadOnly: true).Length == 1;
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

        private void GetBuildingOperationalCapabilities(
            Entity building,
            out bool hasStorageArea,
            out bool hasExtractorArea,
            out bool expandableStorageArea,
            out bool expandableExtractorArea)
        {
            hasStorageArea = false;
            hasExtractorArea = false;
            expandableStorageArea = false;
            expandableExtractorArea = false;
            if (!EntityManager.HasBuffer<Game.Areas.SubArea>(building))
            {
                return;
            }
            DynamicBuffer<Game.Areas.SubArea> subAreas =
                EntityManager.GetBuffer<Game.Areas.SubArea>(building, isReadOnly: true);
            foreach (Game.Areas.SubArea subArea in subAreas)
            {
                Entity area = subArea.m_Area;
                if (area == Entity.Null
                    || !EntityManager.Exists(area)
                    || !IsAreaOwnedBy(area, building))
                {
                    continue;
                }
                bool storage = EntityManager.HasComponent<Game.Areas.Storage>(area);
                bool extractor = EntityManager.HasComponent<Game.Areas.Extractor>(area);
                hasStorageArea |= storage;
                hasExtractorArea |= extractor;
                if (storage
                    && EntityManager.HasBuffer<Game.Areas.Node>(area)
                    && EntityManager.HasComponent<PrefabRef>(area))
                {
                    Entity areaPrefab = EntityManager.GetComponentData<PrefabRef>(area).m_Prefab;
                    int nodeCount = EntityManager.GetBuffer<Game.Areas.Node>(area, isReadOnly: true).Length;
                    expandableStorageArea |= nodeCount >= 4
                        && nodeCount <= 16
                        && EntityManager.HasComponent<StorageAreaData>(areaPrefab)
                        && (EntityManager.GetComponentData<StorageAreaData>(areaPrefab).m_Resources
                            & Game.Economy.Resource.Garbage) != 0;
                }
                if (extractor
                    && EntityManager.HasBuffer<Game.Areas.Node>(area)
                    && EntityManager.HasComponent<PrefabRef>(area))
                {
                    Entity areaPrefab = EntityManager.GetComponentData<PrefabRef>(area).m_Prefab;
                    int nodeCount = EntityManager.GetBuffer<Game.Areas.Node>(area, isReadOnly: true).Length;
                    expandableExtractorArea |= nodeCount >= 4
                        && nodeCount <= 16
                        && EntityManager.HasComponent<ExtractorAreaData>(areaPrefab);
                }
            }
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
                return BridgeResponse.Error(BridgeErrorKind.InvalidArguments, "provide ?index=<int>&version=<int> from /city/buildings");
            }

            if (!TryResolveExistingEntity(index, version, out Entity entity))
            {
                return BridgeResponse.Error(BridgeErrorKind.NotFound, $"entity {index}:{version} does not exist (stale id?)");
            }
            bool isBuilding = EntityManager.HasComponent<Game.Buildings.Building>(entity);
            bool isRoadEdge = false;
            if (EntityManager.HasComponent<Game.Net.Edge>(entity)
                && EntityManager.HasComponent<PrefabRef>(entity))
            {
                Entity prefabEntity = EntityManager.GetComponentData<PrefabRef>(entity).m_Prefab;
                isRoadEdge = EntityManager.HasComponent<RoadData>(prefabEntity);
            }
            if (!isBuilding && !isRoadEdge)
            {
                return BridgeResponse.Error(BridgeErrorKind.InvalidArguments,
                    "demolish only accepts a building from list_buildings or a road segment from list_roads; trees, plants, districts and other network types are unsupported");
            }
            if (EntityManager.HasComponent<Game.Common.Deleted>(entity))
            {
                return BridgeResponse.Error(BridgeErrorKind.Conflict, "entity is already being deleted");
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
                return BridgeResponse.Error(BridgeErrorKind.Conflict, "another build operation is in progress, retry shortly");
            }
            return null;
        }

        private enum UtilityConnectionKind
        {
            None,
            Sewage,
            Water,
            LowVoltage,
        }

        private struct PlacementCapabilities
        {
            public Entity PrefabEntity;
            public BuildingData Building;
            public bool RequiresRoad;
            public bool RequiresShoreline;
            public float ShorelineRadius;
            public float3 PlacementOffset;
            public UtilityConnectionKind UtilityConnection;
            public List<float3> UtilityConnectionPoints;
        }

        private struct PlacementPose
        {
            public float3 Position;
            public quaternion Rotation;
            public float RotationDegrees;
        }

        /// <summary>
        /// Reads placement requirements from prefab data once so every caller
        /// uses the same interface. BuildingData alone does not imply road
        /// access, and shoreline placement is owned by PlaceableObjectData.
        /// </summary>
        private PlacementCapabilities GetPlacementCapabilities(Entity prefabEntity)
        {
            var result = new PlacementCapabilities
            {
                PrefabEntity = prefabEntity,
                ShorelineRadius = 1f,
            };
            if (EntityManager.HasComponent<BuildingData>(prefabEntity))
            {
                result.Building = EntityManager.GetComponentData<BuildingData>(prefabEntity);
                result.ShorelineRadius = math.length(new float2(
                    result.Building.m_LotSize.x,
                    result.Building.m_LotSize.y)) * 4f;
                BuildingFlags flags = result.Building.m_Flags;
                result.RequiresRoad = (flags & BuildingFlags.RequireRoad) != 0;
                if ((flags & BuildingFlags.HasSewageNode) != 0)
                {
                    result.UtilityConnection = UtilityConnectionKind.Sewage;
                }
                else if ((flags & BuildingFlags.HasWaterNode) != 0)
                {
                    result.UtilityConnection = UtilityConnectionKind.Water;
                }
                else if ((flags & BuildingFlags.HasLowVoltageNode) != 0)
                {
                    result.UtilityConnection = UtilityConnectionKind.LowVoltage;
                }
            }
            if (EntityManager.HasComponent<PlaceableObjectData>(prefabEntity))
            {
                PlaceableObjectData placeable =
                    EntityManager.GetComponentData<PlaceableObjectData>(prefabEntity);
                result.RequiresShoreline =
                    (placeable.m_Flags & Game.Objects.PlacementFlags.Shoreline) != 0;
                result.PlacementOffset = placeable.m_PlacementOffset;
            }
            if (!result.RequiresRoad && result.UtilityConnection != UtilityConnectionKind.None)
            {
                result.UtilityConnectionPoints = GetUtilityConnectionPoints(
                    prefabEntity,
                    result.UtilityConnection);
            }
            else if (!result.RequiresRoad)
            {
                result.UtilityConnection = FindOpenUtilityConnection(
                    prefabEntity,
                    out result.UtilityConnectionPoints);
            }
            return result;
        }

        /// <summary>
        /// Some prefabs expose an open utility marker without copying that
        /// fact into BuildingFlags. Treat the marker as the fallback source of
        /// truth while keeping high-voltage networks outside auto-connect.
        /// </summary>
        private UtilityConnectionKind FindOpenUtilityConnection(
            Entity prefabEntity,
            out List<float3> points)
        {
            foreach (UtilityConnectionKind kind in new[]
            {
                UtilityConnectionKind.Sewage,
                UtilityConnectionKind.Water,
                UtilityConnectionKind.LowVoltage,
            })
            {
                points = GetUtilityConnectionPoints(prefabEntity, kind);
                if (points.Count > 0)
                {
                    return kind;
                }
            }

            points = null;
            return UtilityConnectionKind.None;
        }

        private static List<float3> CreateGridSearchSeeds(
            float2 center,
            float radius,
            float step)
        {
            int halfSteps = math.max(1, (int)math.floor(radius / step));
            float radiusSquared = radius * radius;
            var seeds = new List<float3>();
            for (int dz = -halfSteps; dz <= halfSteps; dz++)
            {
                for (int dx = -halfSteps; dx <= halfSteps; dx++)
                {
                    float2 offset = new float2(dx * step, dz * step);
                    if (math.lengthsq(offset) <= radiusSquared)
                    {
                        seeds.Add(new float3(center.x + offset.x, 0f, center.y + offset.y));
                    }
                }
            }
            seeds.Sort((a, b) =>
                math.lengthsq(a.xz - center).CompareTo(math.lengthsq(b.xz - center)));
            return seeds;
        }

        private bool TryResolvePlacement(
            PlacementCapabilities capabilities,
            float3 requestedPosition,
            bool hasExplicitY,
            bool hasExplicitRotation,
            float rotationDegrees,
            ref TerrainHeightData heightData,
            ref WaterSurfaceData<SurfaceWater> waterSurfaceData,
            out PlacementPose pose,
            out string reason)
        {
            if (capabilities.RequiresShoreline)
            {
                return TrySnapShoreline(
                    capabilities,
                    requestedPosition,
                    ref heightData,
                    ref waterSurfaceData,
                    out pose,
                    out reason);
            }

            float3 position = requestedPosition;
            if (!hasExplicitY)
            {
                position.y = TerrainUtils.SampleHeight(ref heightData, position);
            }
            float resolvedRotation = rotationDegrees;
            if (!hasExplicitRotation)
            {
                resolvedRotation = capabilities.RequiresRoad
                    ? AutoRotationTowardsRoad(position)
                    : 0f;
            }
            pose = new PlacementPose
            {
                Position = position,
                RotationDegrees = resolvedRotation,
                Rotation = quaternion.RotateY(math.radians(resolvedRotation)),
            };
            reason = null;
            return true;
        }

        /// <summary>
        /// Mirrors ObjectToolSystem.SnapShoreline: depth threshold 0.2m,
        /// weighted wet/dry centroids, their midpoint, and the prefab placement
        /// offset. The native preview still owns final validation.
        /// </summary>
        private static bool TrySnapShoreline(
            PlacementCapabilities capabilities,
            float3 seed,
            ref TerrainHeightData heightData,
            ref WaterSurfaceData<SurfaceWater> waterSurfaceData,
            out PlacementPose pose,
            out string reason)
        {
            float radius = capabilities.ShorelineRadius;
            int2 min = (int2)math.floor(
                WaterUtils.ToSurfaceSpace(ref waterSurfaceData, seed - radius).xz);
            int2 max = (int2)math.ceil(
                WaterUtils.ToSurfaceSpace(ref waterSurfaceData, seed + radius).xz);
            min = math.max(min, default(int2));
            max = math.min(max, waterSurfaceData.resolution.xz - 1);

            float3 dry = default;
            float3 wet = default;
            float2 surfaceHeight = default;
            for (int z = min.y; z <= max.y; z++)
            {
                for (int x = min.x; x <= max.x; x++)
                {
                    float3 sample = WaterUtils.GetWorldPosition(
                        ref waterSurfaceData,
                        new int2(x, z));
                    float radialWeight = math.max(
                        0f,
                        radius * radius - math.distancesq(sample.xz, seed.xz));
                    if (sample.y > 0.2f)
                    {
                        float waterHeight =
                            TerrainUtils.SampleHeight(ref heightData, sample) + sample.y;
                        sample.y = (sample.y - 0.2f) * radialWeight;
                        sample.xz *= sample.y;
                        wet += sample;
                        waterHeight *= radialWeight;
                        surfaceHeight += new float2(waterHeight, radialWeight);
                    }
                    else if (sample.y < 0.2f)
                    {
                        sample.y = (0.2f - sample.y) * radialWeight;
                        sample.xz *= sample.y;
                        dry += sample;
                    }
                }
            }
            if (dry.y == 0f || wet.y == 0f || surfaceHeight.y == 0f)
            {
                pose = default;
                reason = "no wet/dry shoreline transition was found inside the prefab snap radius";
                return false;
            }

            dry /= dry.y;
            wet /= wet.y;
            float2 direction2 = dry.xz - wet.xz;
            if (math.lengthsq(direction2) < 0.0001f)
            {
                pose = default;
                reason = "shoreline wet/dry centroids do not define a stable direction";
                return false;
            }
            direction2 = math.normalize(direction2);
            float3 direction = new float3(direction2.x, 0f, direction2.y);
            float3 position = new float3
            {
                xz = math.lerp(wet.xz, dry.xz, 0.5f),
                y = surfaceHeight.x / surfaceHeight.y + capabilities.PlacementOffset.y,
            };
            position += direction * capabilities.PlacementOffset.z;
            float resolvedRotation = math.degrees(math.atan2(direction2.x, direction2.y));
            pose = new PlacementPose
            {
                Position = position,
                RotationDegrees = resolvedRotation,
                Rotation = quaternion.LookRotation(direction, math.up()),
            };
            reason = null;
            return true;
        }

        private static string PlacementRetryHint(
            PlacementCapabilities capabilities,
            float searchRadius,
            bool hasRotation)
        {
            string requirement;
            if (capabilities.RequiresRoad && capabilities.RequiresShoreline)
            {
                requirement = "Choose a center near shoreline with road frontage.";
            }
            else if (capabilities.RequiresShoreline)
            {
                requirement = "Choose a center near a wet/dry shoreline transition and within connection range of the city network.";
            }
            else if (capabilities.RequiresRoad)
            {
                requirement = "Choose a center closer to an existing road.";
            }
            else
            {
                requirement = "Choose a center with enough clear, owned land.";
            }

            string rotation = hasRotation
                ? " Remove rotation so the tool can resolve orientation."
                : " Keep rotation omitted so the tool can resolve orientation.";
            if (searchRadius <= 0f)
            {
                return requirement +
                    " Retry with a positive radius instead of the same exact pose." +
                    rotation;
            }
            if (searchRadius < 300f)
            {
                return requirement +
                    $" Increase radius above {searchRadius:F0}m (maximum 300m) or move the center." +
                    rotation;
            }
            return requirement + " Move the center or fix the required road/network layout." + rotation;
        }

        /// <summary>
        /// Resolves the utility connector declared by prefab flags or an open
        /// utility marker. Roadside nodes use the networks carried by the road;
        /// off-road nodes get an explicit connector to that network.
        /// </summary>
        private bool TryResolveAutoConnect(
            PlacementCapabilities capabilities,
            float3 buildingPosition,
            quaternion buildingRotation,
            out Entity netPrefabEntity,
            out PrefabBase netPrefab,
            out float3 connectStart,
            out float3 connectEnd,
            out string reason)
        {
            netPrefabEntity = Entity.Null;
            netPrefab = null;
            connectStart = buildingPosition;
            connectEnd = buildingPosition;
            reason = null;

            if (capabilities.RequiresRoad
                || capabilities.UtilityConnection == UtilityConnectionKind.None)
            {
                return true;
            }

            string netName = null;
            switch (capabilities.UtilityConnection)
            {
                case UtilityConnectionKind.Sewage:
                    netName = "Small Sewage Pipe";
                    break;
                case UtilityConnectionKind.Water:
                    netName = "Small Water Pipe";
                    break;
                case UtilityConnectionKind.LowVoltage:
                    netName = "Low-voltage Ground Cable";
                    break;
            }

            if (!TryFindPrefabByName(NetPrefabQuery, netName, out netPrefabEntity, out netPrefab))
            {
                netPrefabEntity = Entity.Null;
                netPrefab = null;
                reason = $"required connector prefab '{netName}' is unavailable";
                return false;
            }
            if (!TryChooseUtilityConnectionPoint(
                    capabilities,
                    buildingPosition,
                    buildingRotation,
                    out connectStart))
            {
                netPrefabEntity = Entity.Null;
                netPrefab = null;
                reason = $"prefab declares {capabilities.UtilityConnection} but has no open matching SubNet connection node";
                return false;
            }
            if (HasNetNearby(netPrefabEntity, connectStart, 14f))
            {
                // Already connected to this network type nearby; no stub needed.
                netPrefabEntity = Entity.Null;
                netPrefab = null;
                return true;
            }
            if (!TryFindNearestRoadPoint(connectStart, 150f, out connectEnd))
            {
                netPrefabEntity = Entity.Null;
                netPrefab = null;
                reason = $"required {netName} cannot reach the city network: no road is within 150m";
                return false;
            }

            // build_road rejects segments shorter than 8m; extend the stub
            // toward the road if the building sits almost on it.
            float2 delta = connectEnd.xz - connectStart.xz;
            float length = math.length(delta);
            if (length < 8f)
            {
                if (length < 0.5f)
                {
                    netPrefabEntity = Entity.Null;
                    netPrefab = null;
                    return true;
                }
                connectEnd = connectStart + new float3(math.normalizesafe(delta) * 8f, 0f);
            }
            TerrainSystem terrain = World.GetOrCreateSystemManaged<TerrainSystem>();
            TerrainHeightData heightData = terrain.GetHeightData();
            connectEnd.y = TerrainUtils.SampleHeight(ref heightData, connectEnd);
            return true;
        }

        /// <summary>
        /// Returns the prefab-local endpoints that the native object initializer
        /// marked as open for network snapping. Marker data is also the fallback
        /// connection declaration when BuildingFlags omits its summary flag.
        /// </summary>
        private List<float3> GetUtilityConnectionPoints(
            Entity prefabEntity,
            UtilityConnectionKind kind)
        {
            var points = new List<float3>();
            if (!EntityManager.HasBuffer<Game.Prefabs.SubNet>(prefabEntity))
            {
                return points;
            }

            DynamicBuffer<Game.Prefabs.SubNet> subNets =
                EntityManager.GetBuffer<Game.Prefabs.SubNet>(prefabEntity, true);
            foreach (Game.Prefabs.SubNet subNet in subNets)
            {
                if (!IsUtilityMarkerPrefab(subNet.m_Prefab, kind))
                {
                    continue;
                }
                if (subNet.m_Snapping.x)
                {
                    points.Add(subNet.m_Curve.a);
                }
                if (subNet.m_Snapping.y
                    && math.distancesq(subNet.m_Curve.a, subNet.m_Curve.d) > 0.0001f)
                {
                    points.Add(subNet.m_Curve.d);
                }
            }
            return points;
        }

        private bool IsUtilityMarkerPrefab(Entity prefabEntity, UtilityConnectionKind kind)
        {
            if (kind == UtilityConnectionKind.LowVoltage)
            {
                return EntityManager.HasComponent<ElectricityConnectionData>(prefabEntity)
                    && EntityManager.GetComponentData<ElectricityConnectionData>(prefabEntity)
                        .m_Voltage == ElectricityConnection.Voltage.Low;
            }
            if (!EntityManager.HasComponent<WaterPipeConnectionData>(prefabEntity))
            {
                return false;
            }

            WaterPipeConnectionData connection =
                EntityManager.GetComponentData<WaterPipeConnectionData>(prefabEntity);
            switch (kind)
            {
                case UtilityConnectionKind.Sewage:
                    return connection.m_SewageCapacity > 0;
                case UtilityConnectionKind.Water:
                    return connection.m_FreshCapacity > 0;
                default:
                    return false;
            }
        }

        private bool TryChooseUtilityConnectionPoint(
            PlacementCapabilities capabilities,
            float3 buildingPosition,
            quaternion buildingRotation,
            out float3 connectionPoint)
        {
            connectionPoint = buildingPosition;
            if (capabilities.UtilityConnectionPoints == null
                || capabilities.UtilityConnectionPoints.Count == 0)
            {
                return false;
            }

            float bestDistance = float.MaxValue;
            foreach (float3 localPoint in capabilities.UtilityConnectionPoints)
            {
                float3 worldPoint = buildingPosition + math.mul(buildingRotation, localPoint);
                if (!TryFindNearestRoadPoint(worldPoint, 150f, out float3 roadPoint))
                {
                    continue;
                }
                float distance = math.distancesq(worldPoint.xz, roadPoint.xz);
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    connectionPoint = worldPoint;
                }
            }
            return bestDistance < float.MaxValue;
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
            PlacementCapabilities capabilities,
            PlacementPose pose,
            out float roadClearance,
            out string reason)
        {
            Entity prefabEntity = capabilities.PrefabEntity;
            float3 position = pose.Position;
            quaternion rotation = pose.Rotation;
            roadClearance = 0f;
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
            if (capabilities.RequiresRoad)
            {
                BuildingData building = capabilities.Building;
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
                roadClearance = math.max(0f, centerlineDistance - roadHalfWidth);
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
            }
            if (!TryResolveAutoConnect(
                    capabilities,
                    position,
                    rotation,
                    out _,
                    out _,
                    out _,
                    out _,
                    out reason))
            {
                return false;
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

        /// <summary>
        /// Resolves an object that the standalone placement pipeline owns.
        /// Service upgrades share BuildingData/PlaceableObjectData with real
        /// buildings, but their placement interface belongs to the parent
        /// service building and must not leak into these callers.
        /// </summary>
        private bool TryFindStandaloneObjectPrefab(
            string name,
            out Entity prefabEntity,
            out PrefabBase prefab,
            out BridgeResponse error)
        {
            if (!TryFindPrefabByName(BuildingPrefabQuery, name, out prefabEntity, out prefab)
                && !TryFindPrefabByName(TreePrefabQuery, name, out prefabEntity, out prefab))
            {
                error = BridgeResponse.Error(BridgeErrorKind.NotFound,
                    $"unknown building/tree prefab '{name}'; search via /prefabs?category=building|tree&query=...");
                return false;
            }
            if (!IsIndependentBuildingPrefab(prefabEntity))
            {
                error = BridgeResponse.Error(BridgeErrorKind.InvalidArguments,
                    $"prefab '{prefab.name}' is a building upgrade and cannot be placed independently; install it through its owning service building");
                prefabEntity = Entity.Null;
                prefab = null;
                return false;
            }
            error = null;
            return true;
        }

        private bool IsIndependentBuildingPrefab(Entity prefabEntity)
        {
            return !EntityManager.HasComponent<ServiceUpgradeData>(prefabEntity);
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
