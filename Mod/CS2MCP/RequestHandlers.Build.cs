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
        private const float kMapHalfSize = 7168f;
        private const float kMapTileSize = 623.304347826f;
        private const float kPlacementWaterDepth = 0.05f;
        private const int kMaximumPlacementSeeds = 1024;

        private sealed class RoadSurfaceSampler : IRoadSurfaceSampler
        {
            private TerrainHeightData m_HeightData;
            private WaterSurfaceData<SurfaceWater> m_WaterData;

            public RoadSurfaceSampler(
                TerrainHeightData heightData,
                WaterSurfaceData<SurfaceWater> waterData)
            {
                m_HeightData = heightData;
                m_WaterData = waterData;
            }

            public RoadSurfaceSample Sample(float3 roadPosition)
            {
                float waterHeight = WaterUtils.SampleHeight(
                    ref m_WaterData,
                    ref m_HeightData,
                    roadPosition,
                    out float waterDepth);
                return new RoadSurfaceSample(waterHeight, waterDepth);
            }
        }

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
            WaterSystem water = World.GetOrCreateSystemManaged<WaterSystem>();
            WaterSurfaceData<SurfaceWater> waterSurfaceData =
                water.GetSurfaceData(out JobHandle waterDeps);
            waterDeps.Complete();

            float searchRadius = request.TryGetFloat("radius", out float rawRadius)
                ? math.clamp(rawRadius, 8f, 300f)
                : 0f;
            PlacementSearchContext searchContext = CreatePlacementSearchContext(
                capabilities,
                requestedPosition.xz,
                math.max(searchRadius, capabilities.ShorelineRadius));
            PlacementPlan plan;
            if (searchRadius > 0f)
            {
                if (!TryPlanPlacement(
                        capabilities,
                        requestedPosition.xz,
                        searchRadius,
                        hasRotation,
                        rotationDegrees,
                        searchContext,
                        ref heightData,
                        ref waterSurfaceData,
                        out plan,
                        out string searchFailure))
                {
                    return BridgeResponse.Error(BridgeErrorKind.NotFound,
                        $"no valid placement found inside radius {searchRadius:F0}m around ({x:F0},{z:F0}): " +
                        searchFailure + ". " + PlacementRetryHint(
                            capabilities,
                            searchRadius,
                            hasRotation));
                }
            }
            else
            {
                if (!TryResolvePlacement(
                        capabilities,
                        requestedPosition,
                        hasY,
                        hasRotation,
                        rotationDegrees,
                        searchContext,
                        ref heightData,
                        ref waterSurfaceData,
                        out PlacementPose pose,
                        out string resolveReason))
                {
                    return BridgeResponse.Error(BridgeErrorKind.Conflict,
                        $"cannot resolve placement for '{prefab.name}' near ({x:F0},{z:F0}): {resolveReason}. " +
                        PlacementRetryHint(capabilities, searchRadius, hasRotation));
                }
                if (!TryEvaluatePlacement(
                        capabilities,
                        pose,
                        searchContext,
                        ref heightData,
                        ref waterSurfaceData,
                        out float roadClearance,
                        out AutoConnectPlan autoConnect,
                        out string exactReason))
                {
                    return BridgeResponse.Error(BridgeErrorKind.Conflict,
                        $"cannot place '{prefab.name}' at ({x:F0},{z:F0}): {exactReason}. " +
                        PlacementRetryHint(capabilities, searchRadius, hasRotation));
                }
                plan = new PlacementPlan
                {
                    Pose = pose,
                    AutoConnect = autoConnect,
                    RoadClearance = roadClearance,
                };
            }

            BridgeToolSystem tool = World.GetOrCreateSystemManaged<BridgeToolSystem>();
            if (!tool.TryQueuePlacement(
                prefabEntity,
                prefab,
                plan.Pose.Position,
                plan.Pose.Rotation,
                request,
                plan.AutoConnect.PrefabEntity,
                plan.AutoConnect.Prefab,
                plan.AutoConnect.Start,
                plan.AutoConnect.End,
                plan.AutoConnect.TargetEdge,
                plan.AutoConnect.TargetSplit))
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
            WaterSystem water = World.GetOrCreateSystemManaged<WaterSystem>();
            WaterSurfaceData<SurfaceWater> waterSurfaceData =
                water.GetSurfaceData(out JobHandle waterDeps);
            waterDeps.Complete();
            bool hasRotation = request.Query.ContainsKey("rotation");
            float2 center = new float2(x, z);
            PlacementSearchContext searchContext =
                CreatePlacementSearchContext(capabilities, center, radius);
            if (!TryPlanPlacement(
                    capabilities,
                    center,
                    radius,
                    hasRotation,
                    rotationDegrees,
                    searchContext,
                    ref heightData,
                    ref waterSurfaceData,
                    out PlacementPlan plan,
                    out string failure))
            {
                return BridgeResponse.Error(BridgeErrorKind.NotFound,
                    $"no placement candidate could be resolved within {radius:F0}m: {failure}. " +
                    PlacementRetryHint(capabilities, radius, hasRotation));
            }

            BridgeToolSystem tool = World.GetOrCreateSystemManaged<BridgeToolSystem>();
            if (!tool.TryQueueProbe(
                    prefabEntity,
                    prefab,
                    new[] { plan.Pose.Position },
                    plan.Pose.RotationDegrees,
                    request))
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
            public bool PrioritizeForRoad;
        }

        private struct PlacementSeedKey : IEquatable<PlacementSeedKey>
        {
            public int X;
            public int Z;
            public int Rotation;

            public bool Equals(PlacementSeedKey other)
            {
                return X == other.X && Z == other.Z && Rotation == other.Rotation;
            }

            public override bool Equals(object obj)
            {
                return obj is PlacementSeedKey other && Equals(other);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    int hash = X;
                    hash = (hash * 397) ^ Z;
                    return (hash * 397) ^ Rotation;
                }
            }
        }

        private sealed class PlacementPath
        {
            public Entity PrefabEntity;
            public float HalfWidth;
            public float3[] Points;
        }

        private struct PlacementFootprint
        {
            public float2 Center;
            public float2 HalfExtents;
            public float2 Right;
            public float2 Forward;
            public float Radius;
        }

        private sealed class PlacementSearchContext
        {
            public readonly List<PlacementPath> Roads = new List<PlacementPath>();
            public readonly List<PlacementUtilityPath> UtilityPaths = new List<PlacementUtilityPath>();
            public readonly List<PlacementFootprint> Buildings = new List<PlacementFootprint>();
            public readonly HashSet<long> OwnedTiles = new HashSet<long>();
            public ConnectorPrefab Connector;
        }

        private sealed class ConnectorPrefab
        {
            public Entity Entity;
            public PrefabBase Prefab;
            public string Error;
        }

        private struct AutoConnectPlan
        {
            public Entity PrefabEntity;
            public PrefabBase Prefab;
            public float3 Start;
            public float3 End;
            public Entity TargetEdge;
            public float TargetSplit;
            public float Distance;
        }

        private sealed class PlacementPlan
        {
            public PlacementPose Pose;
            public AutoConnectPlan AutoConnect;
            public float DistanceFromCenter;
            public float RoadClearance;
            public int GeneratedCandidates;
            public int PreflightRejected;
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

            WaterSystem water = World.GetOrCreateSystemManaged<WaterSystem>();
            WaterSurfaceData<SurfaceWater> waterSurfaceData =
                water.GetSurfaceData(out JobHandle waterDependencies);
            waterDependencies.Complete();
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
            PlacementSearchContext context =
                CreatePlacementSearchContext(capabilities, center, radius);
            if (!TryPlanPlacement(
                    capabilities,
                    center,
                    radius,
                    hasRotation: false,
                    rotationDegrees: 0f,
                    context,
                    ref heightData,
                    ref waterSurfaceData,
                    out PlacementPlan plan,
                    out _))
            {
                candidate = null;
                return false;
            }
            candidate = new InfrastructureCandidate
            {
                PrefabEntity = capabilities.PrefabEntity,
                Prefab = prefab,
                Position = plan.Pose.Position,
                RotationDegrees = plan.Pose.RotationDegrees,
                ConstructionCost = constructionCost,
                DistanceFromCenter = plan.DistanceFromCenter,
                RoadClearance = plan.RoadClearance,
                GeneratedCandidates = plan.GeneratedCandidates,
                PreflightRejected = plan.PreflightRejected,
            };
            return true;
        }

        private List<PlacementSeed> CreatePlacementSearchSeeds(
            PlacementCapabilities capabilities,
            float2 center,
            float radius,
            float gridStep,
            PlacementSearchContext context,
            ref WaterSurfaceData<SurfaceWater> waterSurfaceData)
        {
            var result = new List<PlacementSeed>();
            if (capabilities.RequiresShoreline)
            {
                result.AddRange(CreateShorelineSearchSeeds(
                    center,
                    radius,
                    ref waterSurfaceData));
            }
            if (capabilities.RequiresRoad && !capabilities.RequiresShoreline)
            {
                result.AddRange(CreateRoadsideSearchSeeds(
                    capabilities.Building,
                    center,
                    radius,
                    context.Roads));
            }
            if (!capabilities.RequiresRoad && !capabilities.RequiresShoreline)
            {
                float boundedStep = math.max(
                    gridStep,
                    radius * math.sqrt(math.PI / (kMaximumPlacementSeeds * 0.9f)));
                foreach (float3 position in CreateGridSearchSeeds(center, radius, boundedStep))
                {
                    result.Add(new PlacementSeed { Position = position });
                }
            }

            if (capabilities.RequiresRoad && capabilities.RequiresShoreline)
            {
                float maximumRoadDistance = capabilities.Building.m_LotSize.y * 4f
                    + Game.Buildings.BuildingUtils.MAX_ROAD_CONNECTION_DISTANCE
                    + capabilities.ShorelineRadius
                    + 32f;
                for (int i = 0; i < result.Count; i++)
                {
                    PlacementSeed seed = result[i];
                    if (TryFindNearestRoadPoint(
                            context,
                            seed.Position,
                            maximumRoadDistance,
                            out _,
                            out _))
                    {
                        seed.PrioritizeForRoad = true;
                        result[i] = seed;
                    }
                }
            }

            var deduplicated = new List<PlacementSeed>();
            var seen = new HashSet<PlacementSeedKey>();
            foreach (PlacementSeed seed in result)
            {
                PlacementSeedKey key = CreatePlacementSeedKey(seed);
                if (seen.Add(key))
                {
                    deduplicated.Add(seed);
                }
            }
            SortPlacementSeeds(deduplicated, center);
            if (deduplicated.Count > kMaximumPlacementSeeds)
            {
                deduplicated.RemoveRange(
                    kMaximumPlacementSeeds,
                    deduplicated.Count - kMaximumPlacementSeeds);
            }
            return deduplicated;
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
            float radius,
            IReadOnlyList<PlacementPath> roads)
        {
            var result = new List<PlacementSeed>();
            foreach (PlacementPath road in roads)
            {
                for (int pointIndex = 0; pointIndex < road.Points.Length; pointIndex++)
                {
                    float3 roadPoint = road.Points[pointIndex];
                    float3 before = road.Points[math.max(0, pointIndex - 1)];
                    float3 after = road.Points[math.min(road.Points.Length - 1, pointIndex + 1)];
                    float2 tangent = math.normalizesafe(after.xz - before.xz);
                    if (math.lengthsq(tangent) < 0.5f)
                    {
                        continue;
                    }
                    float2 normal = new float2(-tangent.y, tangent.x);
                    float offset = road.HalfWidth + building.m_LotSize.y * 4f + 1f;
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
                            PrioritizeForRoad = true,
                        });
                    }
                }
            }
            SortPlacementSeeds(result, center);
            return result;
        }

        private static PlacementSeedKey CreatePlacementSeedKey(PlacementSeed seed)
        {
            return new PlacementSeedKey
            {
                X = (int)math.round(seed.Position.x * 0.5f),
                Z = (int)math.round(seed.Position.z * 0.5f),
                Rotation = seed.HasExplicitRotation
                    ? (int)math.round(NormalizeDegrees(seed.RotationDegrees) * 10f)
                    : -1,
            };
        }

        private static void SortPlacementSeeds(List<PlacementSeed> seeds, float2 center)
        {
            seeds.Sort((first, second) =>
            {
                int byRoadPriority = second.PrioritizeForRoad.CompareTo(first.PrioritizeForRoad);
                if (byRoadPriority != 0)
                {
                    return byRoadPriority;
                }
                int byDistance = math.lengthsq(first.Position.xz - center)
                    .CompareTo(math.lengthsq(second.Position.xz - center));
                if (byDistance != 0)
                {
                    return byDistance;
                }
                int byX = first.Position.x.CompareTo(second.Position.x);
                if (byX != 0)
                {
                    return byX;
                }
                int byZ = first.Position.z.CompareTo(second.Position.z);
                return byZ != 0
                    ? byZ
                    : first.RotationDegrees.CompareTo(second.RotationDegrees);
            });
        }

        /// <summary>
        /// Takes one ECS snapshot for a placement request. Candidate evaluation
        /// then uses stable in-memory geometry instead of repeatedly allocating
        /// and scanning every entity for every seed.
        /// </summary>
        private PlacementSearchContext CreatePlacementSearchContext(
            PlacementCapabilities capabilities,
            float2 center,
            float radius)
        {
            var context = new PlacementSearchContext();
            if (!capabilities.RequiresRoad
                && capabilities.UtilityConnection != UtilityConnectionKind.None)
            {
                context.Connector = ResolveConnectorPrefab(capabilities.UtilityConnection);
            }
            float contextRadius = radius
                + math.max(capabilities.ShorelineRadius, BuildingRadius(capabilities.Building))
                + 180f;
            using (NativeArray<Entity> entities = PlacedRoadQuery.ToEntityArray(Allocator.Temp))
            {
                foreach (Entity entity in entities)
                {
                    PrefabRef prefabRef = EntityManager.GetComponentData<PrefabRef>(entity);
                    Game.Net.Curve curve = EntityManager.GetComponentData<Game.Net.Curve>(entity);
                    float maximumDistance = contextRadius + curve.m_Length;
                    if (math.distancesq(CurveCenter(curve.m_Bezier).xz, center)
                        > maximumDistance * maximumDistance)
                    {
                        continue;
                    }
                    float halfWidth = EntityManager.HasComponent<NetGeometryData>(prefabRef.m_Prefab)
                        ? EntityManager.GetComponentData<NetGeometryData>(prefabRef.m_Prefab)
                            .m_DefaultWidth * 0.5f
                        : 0f;
                    var path = new PlacementPath
                    {
                        PrefabEntity = prefabRef.m_Prefab,
                        HalfWidth = halfWidth,
                        Points = SamplePlacementPath(curve),
                    };
                    if (EntityManager.HasComponent<RoadData>(prefabRef.m_Prefab))
                    {
                        context.Roads.Add(path);
                    }

                    // A top-level net edge owns the longitudinal lanes that can
                    // actually be split by the native net tool. Starting here
                    // excludes object-local and node/vertical connection lanes.
                    if (context.Connector == null
                        || context.Connector.Entity == Entity.Null
                        || !EntityManager.HasBuffer<Game.Net.SubLane>(entity))
                    {
                        continue;
                    }
                    DynamicBuffer<Game.Net.SubLane> subLanes =
                        EntityManager.GetBuffer<Game.Net.SubLane>(entity, isReadOnly: true);
                    foreach (Game.Net.SubLane subLane in subLanes)
                    {
                        Entity lane = subLane.m_SubLane;
                        if (!EntityManager.Exists(lane)
                            || !EntityManager.HasComponent<Game.Net.UtilityLane>(lane)
                            || !EntityManager.HasComponent<Game.Common.Owner>(lane)
                            || !EntityManager.HasComponent<Game.Net.EdgeLane>(lane)
                            || !EntityManager.HasComponent<Game.Net.Curve>(lane)
                            || !EntityManager.HasComponent<PrefabRef>(lane)
                            || EntityManager.HasComponent<Game.Tools.Temp>(lane)
                            || EntityManager.HasComponent<Game.Common.Deleted>(lane))
                        {
                            continue;
                        }
                        Game.Net.UtilityLane utilityLane =
                            EntityManager.GetComponentData<Game.Net.UtilityLane>(lane);
                        if (EntityManager.GetComponentData<Game.Common.Owner>(lane).m_Owner
                                != entity
                            || (utilityLane.m_Flags
                                & Game.Net.UtilityLaneFlags.VerticalConnection) != 0)
                        {
                            continue;
                        }
                        PrefabRef lanePrefab = EntityManager.GetComponentData<PrefabRef>(lane);
                        if (!EntityManager.HasComponent<UtilityLaneData>(lanePrefab.m_Prefab))
                        {
                            continue;
                        }
                        TypedNetworkKinds kinds = ToTypedNetworkKinds(
                            EntityManager.GetComponentData<UtilityLaneData>(lanePrefab.m_Prefab)
                                .m_UtilityTypes);
                        if (kinds == TypedNetworkKinds.None)
                        {
                            continue;
                        }
                        // Kind is on the child lane. Parent CanConnect is the
                        // native edge-snap test: dedicated pipes pass it;
                        // road-carried water/sewage/LV fail it and must attach
                        // at a node, which the net tool still accepts.
                        Game.Net.Curve laneCurve =
                            EntityManager.GetComponentData<Game.Net.Curve>(lane);
                        context.UtilityPaths.Add(new PlacementUtilityPath(
                            kinds,
                            entity,
                            EntityManager.GetComponentData<Game.Net.EdgeLane>(lane).m_EdgeDelta,
                            SamplePlacementPath(laneCurve),
                            !CanConnectUtilityPrefab(
                                context.Connector,
                                prefabRef.m_Prefab)));
                    }
                }
            }

            using (NativeArray<Entity> entities = PlacedBuildingQuery.ToEntityArray(Allocator.Temp))
            {
                foreach (Entity entity in entities)
                {
                    PrefabRef prefabRef = EntityManager.GetComponentData<PrefabRef>(entity);
                    if (!EntityManager.HasComponent<BuildingData>(prefabRef.m_Prefab))
                    {
                        continue;
                    }
                    Transform transform = EntityManager.GetComponentData<Transform>(entity);
                    PlacementFootprint footprint = CreatePlacementFootprint(
                        EntityManager.GetComponentData<BuildingData>(prefabRef.m_Prefab),
                        transform.m_Position,
                        transform.m_Rotation);
                    float maximumDistance = contextRadius + footprint.Radius;
                    if (math.distancesq(footprint.Center, center)
                        <= maximumDistance * maximumDistance)
                    {
                        context.Buildings.Add(footprint);
                    }
                }
            }

            using (NativeArray<Entity> entities = MapTileQuery.ToEntityArray(Allocator.Temp))
            {
                foreach (Entity entity in entities)
                {
                    if (EntityManager.HasComponent<Game.Common.Native>(entity))
                    {
                        continue;
                    }
                    context.OwnedTiles.Add(MapTileKey(
                        EntityManager.GetComponentData<Game.Areas.Geometry>(entity)
                            .m_CenterPosition.xz));
                }
            }

            return context;
        }

        private bool CanConnectUtilityPrefab(
            ConnectorPrefab connector,
            Entity targetNetPrefab)
        {
            if (connector == null
                || connector.Entity == Entity.Null
                || !EntityManager.HasComponent<NetData>(connector.Entity)
                || !EntityManager.HasComponent<NetData>(targetNetPrefab))
            {
                return false;
            }
            return Game.Net.NetUtils.CanConnect(
                EntityManager.GetComponentData<NetData>(connector.Entity),
                EntityManager.GetComponentData<NetData>(targetNetPrefab));
        }

        private static float3[] SamplePlacementPath(Game.Net.Curve curve)
        {
            int segments = math.clamp((int)math.ceil(curve.m_Length / 8f), 1, 192);
            var points = new float3[segments + 1];
            for (int i = 0; i <= segments; i++)
            {
                points[i] = BezierPoint(curve.m_Bezier, i / (float)segments);
            }
            return points;
        }

        private ConnectorPrefab ResolveConnectorPrefab(UtilityConnectionKind kind)
        {
            string name = ConnectorPrefabName(kind);
            if (name == null)
            {
                return new ConnectorPrefab
                {
                    Error = $"unsupported automatic connector kind '{kind}'",
                };
            }
            if (!TryFindPrefabByName(NetPrefabQuery, name, out Entity entity, out PrefabBase prefab))
            {
                return new ConnectorPrefab
                {
                    Error = $"required connector prefab '{name}' is unavailable",
                };
            }
            return new ConnectorPrefab
            {
                Entity = entity,
                Prefab = prefab,
            };
        }

        private static string ConnectorPrefabName(UtilityConnectionKind kind)
        {
            switch (kind)
            {
                case UtilityConnectionKind.Sewage:
                    return "Small Sewage Pipe";
                case UtilityConnectionKind.Water:
                    return "Small Water Pipe";
                case UtilityConnectionKind.LowVoltage:
                    return "Low-voltage Ground Cable";
                default:
                    return null;
            }
        }

        private static TypedNetworkKinds ToTypedNetworkKinds(UtilityConnectionKind kind)
        {
            switch (kind)
            {
                case UtilityConnectionKind.Water:
                    return TypedNetworkKinds.Water;
                case UtilityConnectionKind.Sewage:
                    return TypedNetworkKinds.Sewage;
                case UtilityConnectionKind.LowVoltage:
                    return TypedNetworkKinds.LowVoltage;
                default:
                    return TypedNetworkKinds.None;
            }
        }

        /// <summary>
        /// Resolves, preflights and ranks every bounded internal candidate, but
        /// returns one finalist. Native validation remains a single transaction.
        /// </summary>
        private bool TryPlanPlacement(
            PlacementCapabilities capabilities,
            float2 center,
            float radius,
            bool hasRotation,
            float rotationDegrees,
            PlacementSearchContext context,
            ref TerrainHeightData heightData,
            ref WaterSurfaceData<SurfaceWater> waterSurfaceData,
            out PlacementPlan selected,
            out string failure)
        {
            List<PlacementSeed> seeds = CreatePlacementSearchSeeds(
                capabilities,
                center,
                radius,
                8f,
                context,
                ref waterSurfaceData);
            var candidates = new List<PlacementPlan>();
            var rejectedReasons = new Dictionary<string, int>(StringComparer.Ordinal);
            var resolved = new HashSet<PlacementSeedKey>();
            int rejected = 0;
            foreach (PlacementSeed seed in seeds)
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
                        context,
                        ref heightData,
                        ref waterSurfaceData,
                        out PlacementPose pose,
                        out string reason))
                {
                    rejected++;
                    AddPlacementRejection(rejectedReasons, reason);
                    continue;
                }
                if (math.distance(pose.Position.xz, center) > radius + 0.5f)
                {
                    rejected++;
                    AddPlacementRejection(
                        rejectedReasons,
                        "shoreline snapping moved the resolved footprint outside the requested radius");
                    continue;
                }
                var resolvedSeed = new PlacementSeed
                {
                    Position = pose.Position,
                    HasExplicitRotation = true,
                    RotationDegrees = pose.RotationDegrees,
                };
                if (!resolved.Add(CreatePlacementSeedKey(resolvedSeed)))
                {
                    continue;
                }
                if (!TryEvaluatePlacement(
                        capabilities,
                        pose,
                        context,
                        ref heightData,
                        ref waterSurfaceData,
                        out float roadClearance,
                        out AutoConnectPlan autoConnect,
                        out reason))
                {
                    rejected++;
                    AddPlacementRejection(rejectedReasons, reason);
                    continue;
                }

                float distance = math.distance(pose.Position.xz, center);
                candidates.Add(new PlacementPlan
                {
                    Pose = pose,
                    AutoConnect = autoConnect,
                    DistanceFromCenter = distance,
                    RoadClearance = roadClearance,
                });
            }

            if (candidates.Count == 0)
            {
                selected = null;
                failure = FormatPlacementRejections(seeds.Count, resolved.Count, rejectedReasons);
                return false;
            }
            candidates.Sort(ComparePlacementPlans);
            selected = candidates[0];
            selected.GeneratedCandidates = seeds.Count;
            selected.PreflightRejected = rejected;
            failure = null;
            return true;
        }

        private static int ComparePlacementPlans(PlacementPlan first, PlacementPlan second)
        {
            float firstScore = first.DistanceFromCenter
                + first.RoadClearance * 2f
                + first.AutoConnect.Distance * 0.25f;
            float secondScore = second.DistanceFromCenter
                + second.RoadClearance * 2f
                + second.AutoConnect.Distance * 0.25f;
            int byScore = firstScore.CompareTo(secondScore);
            if (byScore != 0)
            {
                return byScore;
            }
            int byDistance = first.DistanceFromCenter.CompareTo(second.DistanceFromCenter);
            if (byDistance != 0)
            {
                return byDistance;
            }
            int byX = first.Pose.Position.x.CompareTo(second.Pose.Position.x);
            if (byX != 0)
            {
                return byX;
            }
            int byZ = first.Pose.Position.z.CompareTo(second.Pose.Position.z);
            return byZ != 0
                ? byZ
                : first.Pose.RotationDegrees.CompareTo(second.Pose.RotationDegrees);
        }

        private static void AddPlacementRejection(
            Dictionary<string, int> reasons,
            string reason)
        {
            reason = string.IsNullOrWhiteSpace(reason)
                ? "candidate failed preflight for an unspecified reason"
                : reason.Trim();
            reasons.TryGetValue(reason, out int count);
            reasons[reason] = count + 1;
        }

        private static string FormatPlacementRejections(
            int generated,
            int resolved,
            Dictionary<string, int> reasons)
        {
            if (generated == 0)
            {
                return "generated 0 candidates for the prefab's placement requirements";
            }
            var ranked = new List<KeyValuePair<string, int>>(reasons);
            ranked.Sort((first, second) =>
            {
                int byCount = second.Value.CompareTo(first.Value);
                return byCount != 0
                    ? byCount
                    : string.Compare(first.Key, second.Key, StringComparison.Ordinal);
            });
            int count = math.min(3, ranked.Count);
            var summaries = new List<string>(count);
            for (int i = 0; i < count; i++)
            {
                summaries.Add($"{ranked[i].Value}x {ranked[i].Key}");
            }
            string reasonSummary = summaries.Count > 0
                ? string.Join("; ", summaries)
                : "all resolved candidates were duplicates";
            return $"generated {generated} seeds and {resolved} distinct resolved poses; " +
                $"leading rejections: {reasonSummary}";
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

            bool isRoad = EntityManager.HasComponent<RoadData>(prefabEntity);
            if (!NetworkBuildArguments.TryParse(
                    request.Query,
                    isRoad,
                    out NetworkBuildArguments arguments,
                    out string argumentError))
            {
                return BridgeResponse.Error(
                    BridgeErrorKind.InvalidArguments,
                    argumentError);
            }
            RoadBuildMode? roadMode = arguments.RoadMode;

            TerrainSystem terrain = World.GetOrCreateSystemManaged<TerrainSystem>();
            TerrainHeightData heightData = terrain.GetHeightData();
            float3 start = new float3(x1, 0f, z1);
            start.y = TerrainUtils.SampleHeight(ref heightData, start);
            float3 end = new float3(x2, 0f, z2);
            end.y = TerrainUtils.SampleHeight(ref heightData, end);

            bool hasMid = arguments.HasControlPoint;
            float3 mid = default;
            if (hasMid)
            {
                mid = new float3(arguments.ControlX, 0f, arguments.ControlZ);
                mid.y = TerrainUtils.SampleHeight(ref heightData, mid);
            }

            float e1 = arguments.StartElevation;
            float e2 = arguments.EndElevation;
            if (!arguments.HasElevation
                && IsBuriedNetPrefab(prefab.name))
            {
                // Pipes and ground cables are underground networks: default to
                // -10m so they are actually buried instead of floating on the
                // surface.
                e1 = -10f;
                e2 = -10f;
            }
            var elevations = new float2(e1, e2);

            RoadPath roadPath = default;
            if (isRoad)
            {
                RoadPath requestedPath = hasMid
                    ? RoadPath.WithControlPoint(start, mid, end)
                    : RoadPath.Straight(start, end);
                Game.Net.Curve rawCurve = new Game.Net.Curve
                {
                    m_Bezier = new Bezier4x3(
                        requestedPath.A,
                        requestedPath.B,
                        requestedPath.C,
                        requestedPath.D),
                };
                Bezier4x3 adjusted = Game.Net.NetUtils.AdjustPosition(
                    rawCurve,
                    fixedStart: false,
                    linearMiddle: false,
                    fixedEnd: false,
                    ref heightData).m_Bezier;
                roadPath = new RoadPath(adjusted.a, adjusted.b, adjusted.c, adjusted.d);
            }

            if (roadMode == RoadBuildMode.Ground)
            {
                if (!EntityManager.HasComponent<NetGeometryData>(prefabEntity)
                    || !EntityManager.HasComponent<PlaceableNetData>(prefabEntity))
                {
                    return BridgeResponse.Error(
                        BridgeErrorKind.Conflict,
                        $"road prefab '{prefab.name}' lacks the native geometry or placement data required for ground-path validation");
                }

                PlaceableNetData placeable = EntityManager.GetComponentData<PlaceableNetData>(prefabEntity);
                if ((placeable.m_PlacementFlags & Game.Net.PlacementFlags.OnGround) == 0)
                {
                    return BridgeResponse.Error(
                        BridgeErrorKind.InvalidArguments,
                        $"road prefab '{prefab.name}' does not support mode=ground; use mode=grade-separated with both e1/e2 if appropriate");
                }

                WaterSystem water = World.GetOrCreateSystemManaged<WaterSystem>();
                WaterSurfaceData<SurfaceWater> waterData =
                    water.GetSurfaceData(out JobHandle waterDependencies);
                waterDependencies.Complete();
                NetGeometryData geometry = EntityManager.GetComponentData<NetGeometryData>(prefabEntity);
                RoadGroundPreflightResult preflight = RoadGroundPreflight.Evaluate(
                    roadPath,
                    geometry.m_DefaultWidth * 0.5f,
                    geometry.m_MaxSlopeSteepness,
                    geometry.m_DefaultHeightRange.min,
                    new RoadSurfaceSampler(heightData, waterData));
                if (!preflight.Allowed)
                {
                    if (preflight.Block == RoadGroundBlock.Water)
                    {
                        return BridgeResponse.Error(
                            BridgeErrorKind.Conflict,
                            $"mode=ground route crosses water near ({preflight.Position.x:F1}, {preflight.Position.z:F1}) " +
                            $"(depth {preflight.WaterDepth:F2}m); choose a dry route, or explicitly use mode=grade-separated with both e1/e2 for an intentional crossing");
                    }
                    if (preflight.Block == RoadGroundBlock.InvalidPath)
                    {
                        return BridgeResponse.Error(
                            BridgeErrorKind.InvalidArguments,
                            "mode=ground route is too long or non-finite to validate; move cx/cz closer to the endpoints or split the road into shorter segments");
                    }
                    return BridgeResponse.Error(
                        BridgeErrorKind.Conflict,
                        $"mode=ground route is too steep near ({preflight.Position.x:F1}, {preflight.Position.z:F1}): " +
                        $"observed {preflight.Grade * 100f:F1}%, allowed {preflight.MaximumGrade * 100f:F1}% " +
                        "(10% product ceiling or a stricter prefab limit); " +
                        "choose a gentler route, or explicitly use mode=grade-separated with both e1/e2");
                }
            }

            BridgeToolSystem tool = World.GetOrCreateSystemManaged<BridgeToolSystem>();
            if (!tool.TryQueueRoad(
                    prefabEntity,
                    prefab,
                    start,
                    end,
                    mid,
                    hasMid,
                    elevations,
                    roadMode,
                    roadPath,
                    request))
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
                return BridgeResponse.Error(BridgeErrorKind.InvalidArguments, "provide ?index=&version= of a road segment from list_networks");
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
                    "provide ?index=&version= of a standalone road segment from list_networks");
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
                    List<string> roles = GetPrefabRoles(prefabRef.m_Prefab);
                    if (!string.IsNullOrEmpty(requestedRole) && !roles.Contains(requestedRole))
                    {
                        continue;
                    }
                    Transform transform = EntityManager.GetComponentData<Transform>(entity);
                    if (hasCenter && math.distance(transform.m_Position.xz, center) > radius)
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
                        roles,
                        capabilities = new
                        {
                            operationalArea = hasStorageArea || hasExtractorArea,
                            storageArea = hasStorageArea,
                            extractorArea = hasExtractorArea,
                            expandableStorageArea,
                            expandableExtractorArea,
                        },
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
            bool isTypedNetwork = false;
            if (EntityManager.HasComponent<Game.Net.Edge>(entity)
                && EntityManager.HasComponent<PrefabRef>(entity))
            {
                TypedNetworkKinds kinds = ClassifyNetPrefab(
                    EntityManager.GetComponentData<PrefabRef>(entity).m_Prefab);
                isTypedNetwork = (kinds & TypedNetworkMath.ProductKinds) != TypedNetworkKinds.None;
            }
            if (!isBuilding && !isTypedNetwork)
            {
                return BridgeResponse.Error(BridgeErrorKind.InvalidArguments,
                    "demolish only accepts a building from list_buildings or a typed-network edge from list_networks (road, water, sewage, low_voltage); trees, plants, districts, tracks and other nets are unsupported");
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
            public bool RejectsWater;
            public bool AllowsObjectOverlap;
            public bool AllowsRoadOverlap;
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
                result.AllowsRoadOverlap =
                    (flags & (BuildingFlags.CanBeOnRoad | BuildingFlags.CanBeOnRoadArea)) != 0;
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
                Game.Objects.PlacementFlags flags = placeable.m_Flags;
                result.RequiresShoreline =
                    (flags & Game.Objects.PlacementFlags.Shoreline) != 0;
                result.AllowsObjectOverlap =
                    (flags & Game.Objects.PlacementFlags.CanOverlap) != 0;
                Game.Objects.PlacementFlags waterPlacement =
                    Game.Objects.PlacementFlags.Shoreline
                    | Game.Objects.PlacementFlags.Floating
                    | Game.Objects.PlacementFlags.Hovering
                    | Game.Objects.PlacementFlags.Underwater
                    | Game.Objects.PlacementFlags.Waterway;
                result.RejectsWater =
                    (flags & Game.Objects.PlacementFlags.OnGround) != 0
                    && (flags & waterPlacement) == 0;
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
            int halfSteps = math.max(1, (int)math.ceil(radius / step));
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
            PlacementSearchContext context,
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
                    ? AutoRotationTowardsRoad(context, position)
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

        private static float3 BezierPoint(Bezier4x3 bezier, float t)
        {
            float3 ab = math.lerp(bezier.a, bezier.b, t);
            float3 bc = math.lerp(bezier.b, bezier.c, t);
            float3 cd = math.lerp(bezier.c, bezier.d, t);
            float3 abc = math.lerp(ab, bc, t);
            float3 bcd = math.lerp(bc, cd, t);
            return math.lerp(abc, bcd, t);
        }

        private static float3 CurveCenter(Bezier4x3 bezier)
        {
            return BezierPoint(bezier, 0.5f);
        }

        private static float NormalizeDegrees(float degrees)
        {
            degrees %= 360f;
            return degrees < 0f ? degrees + 360f : degrees;
        }

        private static long MapTileKey(float2 position)
        {
            int x = (int)Math.Floor((position.x + kMapHalfSize) / kMapTileSize);
            int z = (int)Math.Floor((position.y + kMapHalfSize) / kMapTileSize);
            return ((long)x << 32) | (uint)z;
        }

        private static float BuildingRadius(BuildingData building)
        {
            return math.length(new float2(building.m_LotSize)) * 4f;
        }

        private float BuildingRadius(Entity prefabEntity)
        {
            return EntityManager.HasComponent<BuildingData>(prefabEntity)
                ? BuildingRadius(EntityManager.GetComponentData<BuildingData>(prefabEntity))
                : 0f;
        }

        private bool IsOnOwnedTile(float3 position)
        {
            long key = MapTileKey(position.xz);
            using (NativeArray<Entity> entities = MapTileQuery.ToEntityArray(Allocator.Temp))
            {
                foreach (Entity entity in entities)
                {
                    if (MapTileKey(EntityManager.GetComponentData<Game.Areas.Geometry>(entity)
                            .m_CenterPosition.xz) == key)
                    {
                        return !EntityManager.HasComponent<Game.Common.Native>(entity);
                    }
                }
            }
            return false;
        }

        private static PlacementFootprint CreatePlacementFootprint(
            BuildingData building,
            float3 position,
            quaternion rotation)
        {
            float3 right3 = math.mul(rotation, new float3(1f, 0f, 0f));
            float3 forward3 = math.forward(rotation);
            float2 right = math.normalizesafe(right3.xz, new float2(1f, 0f));
            float2 forward = math.normalizesafe(forward3.xz, new float2(0f, 1f));
            float2 halfExtents = new float2(building.m_LotSize) * 4f;
            return new PlacementFootprint
            {
                Center = position.xz,
                HalfExtents = halfExtents,
                Right = right,
                Forward = forward,
                Radius = math.length(halfExtents),
            };
        }

        private static bool TryFindNearestRoadPoint(
            PlacementSearchContext context,
            float3 from,
            float maxDistance,
            out float3 nearest,
            out float roadHalfWidth)
        {
            nearest = from;
            roadHalfWidth = 0f;
            if (context == null)
            {
                return false;
            }
            float bestClearance = maxDistance;
            bool found = false;
            foreach (PlacementPath road in context.Roads)
            {
                for (int i = 1; i < road.Points.Length; i++)
                {
                    float amount = PlacementSearchMath.ClosestPointAmount(
                        from.xz,
                        road.Points[i - 1].xz,
                        road.Points[i].xz);
                    float3 closest = math.lerp(
                        road.Points[i - 1],
                        road.Points[i],
                        amount);
                    float clearance = math.distance(closest.xz, from.xz) - road.HalfWidth;
                    if (clearance < bestClearance)
                    {
                        bestClearance = clearance;
                        nearest = closest;
                        roadHalfWidth = road.HalfWidth;
                        found = true;
                    }
                }
            }
            return found;
        }

        private float AutoRotationTowardsRoad(
            PlacementSearchContext context,
            float3 position)
        {
            if (TryFindNearestRoadPoint(
                    context,
                    position,
                    200f,
                    out float3 roadPoint,
                    out _))
            {
                float2 delta = roadPoint.xz - position.xz;
                if (math.lengthsq(delta) > 0.25f)
                {
                    return math.degrees(math.atan2(delta.x, delta.y));
                }
            }
            return 0f;
        }

        private bool TryEvaluatePlacement(
            PlacementCapabilities capabilities,
            PlacementPose pose,
            PlacementSearchContext context,
            ref TerrainHeightData heightData,
            ref WaterSurfaceData<SurfaceWater> waterSurfaceData,
            out float roadClearance,
            out AutoConnectPlan autoConnect,
            out string reason)
        {
            roadClearance = 0f;
            autoConnect = default;
            PlacementFootprint footprint = CreatePlacementFootprint(
                capabilities.Building,
                pose.Position,
                pose.Rotation);
            if (!IsFootprintOnOwnedTiles(context, footprint))
            {
                reason = "building footprint crosses outside owned map tiles (buy a tile first)";
                return false;
            }
            if (!capabilities.AllowsObjectOverlap
                && OverlapsExistingBuilding(context, footprint))
            {
                reason = "building footprint overlaps an existing building";
                return false;
            }
            if (!capabilities.AllowsRoadOverlap
                && OverlapsExistingRoad(context, footprint))
            {
                reason = "building footprint overlaps an existing road";
                return false;
            }
            if (capabilities.RejectsWater
                && FootprintTouchesWater(footprint, ref waterSurfaceData))
            {
                reason = "ordinary ground building footprint intersects water";
                return false;
            }
            if (capabilities.RequiresRoad
                && !TryValidateRoadFrontage(
                    capabilities,
                    pose,
                    context,
                    out roadClearance,
                    out reason))
            {
                return false;
            }
            return TryPlanAutoConnect(
                capabilities,
                pose,
                context,
                ref heightData,
                out autoConnect,
                out reason);
        }

        private static bool IsFootprintOnOwnedTiles(
            PlacementSearchContext context,
            PlacementFootprint footprint)
        {
            float2 right = footprint.Right * footprint.HalfExtents.x;
            float2 forward = footprint.Forward * footprint.HalfExtents.y;
            float2[] corners =
            {
                footprint.Center + right + forward,
                footprint.Center + right - forward,
                footprint.Center - right + forward,
                footprint.Center - right - forward,
            };
            if (!context.OwnedTiles.Contains(MapTileKey(footprint.Center)))
            {
                return false;
            }
            foreach (float2 corner in corners)
            {
                if (!context.OwnedTiles.Contains(MapTileKey(corner)))
                {
                    return false;
                }
                if (!context.OwnedTiles.Contains(MapTileKey(math.lerp(
                        footprint.Center,
                        corner,
                        0.5f))))
                {
                    return false;
                }
            }
            return true;
        }

        private static bool OverlapsExistingBuilding(
            PlacementSearchContext context,
            PlacementFootprint candidate)
        {
            foreach (PlacementFootprint existing in context.Buildings)
            {
                float maximumDistance = candidate.Radius + existing.Radius;
                if (math.distancesq(candidate.Center, existing.Center)
                    >= maximumDistance * maximumDistance)
                {
                    continue;
                }
                if (PlacementSearchMath.OrientedBoxesOverlap(
                    candidate.Center,
                    candidate.HalfExtents,
                    candidate.Right,
                    candidate.Forward,
                    existing.Center,
                    existing.HalfExtents,
                    existing.Right,
                    existing.Forward))
                {
                    return true;
                }
            }
            return false;
        }

        private static bool OverlapsExistingRoad(
            PlacementSearchContext context,
            PlacementFootprint footprint)
        {
            foreach (PlacementPath road in context.Roads)
            {
                for (int i = 1; i < road.Points.Length; i++)
                {
                    if (PlacementSearchMath.SegmentIntersectsExpandedBox(
                            road.Points[i - 1].xz,
                            road.Points[i].xz,
                            footprint.Center,
                            footprint.HalfExtents,
                            footprint.Right,
                            footprint.Forward,
                            road.HalfWidth))
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        private static bool FootprintTouchesWater(
            PlacementFootprint footprint,
            ref WaterSurfaceData<SurfaceWater> waterSurfaceData)
        {
            int columns = math.clamp(
                (int)math.ceil(footprint.HalfExtents.x * 2f / kFootprintCellSize),
                1,
                32);
            int rows = math.clamp(
                (int)math.ceil(footprint.HalfExtents.y * 2f / kFootprintCellSize),
                1,
                32);
            for (int row = 0; row <= rows; row++)
            {
                float forwardOffset = math.lerp(
                    -footprint.HalfExtents.y,
                    footprint.HalfExtents.y,
                    row / (float)rows);
                for (int column = 0; column <= columns; column++)
                {
                    float rightOffset = math.lerp(
                        -footprint.HalfExtents.x,
                        footprint.HalfExtents.x,
                        column / (float)columns);
                    float2 position = footprint.Center
                        + footprint.Right * rightOffset
                        + footprint.Forward * forwardOffset;
                    if (IsPlacementWater(position, ref waterSurfaceData))
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        private static bool IsPlacementWater(
            float2 position,
            ref WaterSurfaceData<SurfaceWater> waterSurfaceData)
        {
            return WaterUtils.SampleDepth(
                ref waterSurfaceData,
                new float3(position.x, 0f, position.y)) > kPlacementWaterDepth;
        }

        private static bool TryValidateRoadFrontage(
            PlacementCapabilities capabilities,
            PlacementPose pose,
            PlacementSearchContext context,
            out float roadClearance,
            out string reason)
        {
            float3 front = pose.Position
                + math.forward(pose.Rotation) * (capabilities.Building.m_LotSize.y * 4f);
            float maximumDistance = Game.Buildings.BuildingUtils.MAX_ROAD_CONNECTION_DISTANCE;
            if (!TryFindNearestRoadPoint(
                    context,
                    front,
                    maximumDistance,
                    out float3 roadPoint,
                    out float roadHalfWidth))
            {
                roadClearance = 0f;
                reason = $"building frontage is more than {maximumDistance:F1}m from a road (build a road to the site first)";
                return false;
            }
            float centerlineDistance = math.distance(front.xz, roadPoint.xz);
            roadClearance = math.max(0f, centerlineDistance - roadHalfWidth);
            if (centerlineDistance < roadHalfWidth - 2f)
            {
                reason = "building frontage overlaps the road center area";
                return false;
            }
            if (roadClearance > maximumDistance)
            {
                reason = $"building frontage is more than {maximumDistance:F1}m from a road (build a road to the site first)";
                return false;
            }
            reason = null;
            return true;
        }

        private bool TryPlanAutoConnect(
            PlacementCapabilities capabilities,
            PlacementPose pose,
            PlacementSearchContext context,
            ref TerrainHeightData heightData,
            out AutoConnectPlan plan,
            out string reason)
        {
            plan = default;
            reason = null;
            if (capabilities.RequiresRoad
                || capabilities.UtilityConnection == UtilityConnectionKind.None)
            {
                return true;
            }
            ConnectorPrefab connector = context.Connector;
            if (connector == null)
            {
                reason = $"required connector metadata for {capabilities.UtilityConnection} is unavailable";
                return false;
            }
            if (!string.IsNullOrEmpty(connector.Error))
            {
                reason = connector.Error;
                return false;
            }
            if (HasMatchingNetAtAnyConnectionPoint(
                    capabilities,
                    pose.Position,
                    pose.Rotation,
                    context,
                    14f))
            {
                return true;
            }
            if (!TryChooseUtilityConnectionPoint(
                    capabilities,
                    pose.Position,
                    pose.Rotation,
                    context,
                    out float3 start,
                    out UtilityConnectionTarget target))
            {
                reason = $"prefab declares {capabilities.UtilityConnection} but no open matching connection node can reach the corresponding utility network within 150m";
                return false;
            }
            Game.Net.Curve parentCurve =
                EntityManager.GetComponentData<Game.Net.Curve>(target.ParentEdge);
            float3 end = BezierPoint(parentCurve.m_Bezier, target.ParentSplit);
            float2 delta = end.xz - start.xz;
            if (math.length(delta) < 0.5f)
            {
                return true;
            }
            end.y = TerrainUtils.SampleHeight(ref heightData, end);
            float length = math.distance(start.xz, end.xz);
            plan = new AutoConnectPlan
            {
                PrefabEntity = connector.Entity,
                Prefab = connector.Prefab,
                Start = start,
                End = end,
                TargetEdge = target.ParentEdge,
                TargetSplit = target.ParentSplit,
                Distance = length,
            };
            return true;
        }

        private static bool TryChooseUtilityConnectionPoint(
            PlacementCapabilities capabilities,
            float3 buildingPosition,
            quaternion buildingRotation,
            PlacementSearchContext context,
            out float3 connectionPoint,
            out UtilityConnectionTarget target)
        {
            connectionPoint = buildingPosition;
            target = default;
            if (capabilities.UtilityConnectionPoints == null
                || capabilities.UtilityConnectionPoints.Count == 0)
            {
                return false;
            }
            TypedNetworkKinds required = ToTypedNetworkKinds(
                capabilities.UtilityConnection);
            float bestDistanceSquared = float.MaxValue;
            foreach (float3 localPoint in capabilities.UtilityConnectionPoints)
            {
                float3 worldPoint = buildingPosition + math.mul(buildingRotation, localPoint);
                if (!PlacementSearchMath.TryFindNearestUtilityPoint(
                        context.UtilityPaths,
                        required,
                        worldPoint,
                        150f,
                        out UtilityConnectionTarget candidate))
                {
                    continue;
                }
                float distanceSquared = math.distancesq(
                    worldPoint.xz,
                    candidate.Position.xz);
                if (distanceSquared < bestDistanceSquared)
                {
                    bestDistanceSquared = distanceSquared;
                    connectionPoint = worldPoint;
                    target = candidate;
                }
            }
            return bestDistanceSquared < float.MaxValue;
        }

        private static bool HasMatchingNetAtAnyConnectionPoint(
            PlacementCapabilities capabilities,
            float3 buildingPosition,
            quaternion buildingRotation,
            PlacementSearchContext context,
            float radius)
        {
            if (capabilities.UtilityConnectionPoints == null)
            {
                return false;
            }
            TypedNetworkKinds required = ToTypedNetworkKinds(
                capabilities.UtilityConnection);
            foreach (float3 localPoint in capabilities.UtilityConnectionPoints)
            {
                float3 worldPoint = buildingPosition + math.mul(buildingRotation, localPoint);
                if (PlacementSearchMath.TryFindNearestUtilityPoint(
                    context.UtilityPaths,
                    required,
                    worldPoint,
                    radius,
                    out _))
                {
                    return true;
                }
            }
            return false;
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
