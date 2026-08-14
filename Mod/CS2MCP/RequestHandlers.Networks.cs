using System;
using System.Collections.Generic;
using Game.Prefabs;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace CS2MCP
{
    /// <summary>
    /// Typed-network list, topology QA, and the shared prefab classification
    /// used by auto-connect and demolish.
    /// </summary>
    public sealed partial class RequestHandlers
    {
        private const int kNetworkFindingCap = 64;
        private const int kNetworkListDefaultLimit = 16;
        private const int kNetworkListHardMax = 64;

        private BridgeResponse ListNetworks(BridgeRequest request)
        {
            if (!TryGetCity(out _, out BridgeResponse error))
            {
                return error;
            }
            request.Query.TryGetValue("kind", out string kindRaw);
            if (!TypedNetworkMath.TryParseKind(kindRaw, out TypedNetworkKinds filter, out string kindError))
            {
                return BridgeResponse.Error(BridgeErrorKind.InvalidArguments, kindError);
            }

            request.Query.TryGetValue("query", out string search);
            int limit = request.TryGetInt("limit", out int rawLimit)
                ? math.clamp(rawLimit, 1, kNetworkListHardMax)
                : kNetworkListDefaultLimit;
            if (!TryGetOptionalCenter(request, out bool hasCenter, out float2 center, out BridgeResponse centerError))
            {
                return centerError;
            }
            float radius = request.TryGetFloat("radius", out float rawRadius) ? math.max(rawRadius, 1f) : 250f;
            request.Query.TryGetValue("sort", out string sortRaw);
            if (!TypedNetworkMath.TryParseNetworkSort(
                    sortRaw,
                    filter,
                    hasCenter,
                    out string sort,
                    out string sortError))
            {
                return BridgeResponse.Error(BridgeErrorKind.InvalidArguments, sortError);
            }
            bool ranked = hasCenter || !string.IsNullOrEmpty(sort);

            List<TypedNetworkEdge> snapshot = SnapshotTypedNetworks(
                hasCenter ? (float2?)center : null,
                radius);

            PrefabSystem prefabSystem = World.GetOrCreateSystemManaged<PrefabSystem>();
            var found = new List<(float rank, object item)>();
            int total = 0;
            for (int i = 0; i < snapshot.Count; i++)
            {
                TypedNetworkEdge edge = snapshot[i];
                if (!TypedNetworkMath.MatchesFilter(edge.Kinds, filter))
                {
                    continue;
                }
                var entity = new Entity { Index = edge.EntityIndex, Version = edge.EntityVersion };
                string name = GetEntityPrefabName(prefabSystem, entity) ?? "<unknown>";
                if (!string.IsNullOrEmpty(search)
                    && name.IndexOf(search, StringComparison.OrdinalIgnoreCase) < 0)
                {
                    continue;
                }
                total++;
                float distance = hasCenter
                    ? math.distance(
                        edge.Points[0].xz * 0.5f + edge.Points[edge.Points.Length - 1].xz * 0.5f,
                        center)
                    : 0f;
                float volumeIndex = 0f;
                float congestionIndex = 0f;
                float loadRatio = 0f;
                object traffic = null;
                if (filter == TypedNetworkKinds.Road)
                {
                    traffic = ReadRoadTraffic(entity, out volumeIndex, out congestionIndex);
                }
                // TODO(windows): low_voltage electricity{flow,capacity,bottleneck}
                // — see docs/ops/2026-08-15-windows-game-dll-handoff.md
                float rank = TypedNetworkMath.NetworkListRank(
                    sort,
                    distance,
                    volumeIndex,
                    congestionIndex,
                    loadRatio);
                float? widthM = filter == TypedNetworkKinds.Road
                    ? NetworkWidthM(
                        EntityManager,
                        EntityManager.GetComponentData<PrefabRef>(entity).m_Prefab)
                    : null;
                object item = BuildNetworkListItem(
                    edge,
                    name,
                    filter,
                    hasCenter,
                    distance,
                    widthM,
                    traffic);
                if (found.Count < limit)
                {
                    found.Add((rank, item));
                }
                else if (ranked)
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
                        found[worst] = (rank, item);
                    }
                }
            }

            if (ranked && found.Count > 1)
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
                kind = TypedNetworkMath.PrimaryKindName(filter),
                sort,
                truncated,
                warning = truncated
                    ? $"too many results: {total} network edges match, only {results.Count} returned; shrink radius or add a query filter."
                    : null,
                note = "inventory only, no topology; demolish accepts entity ids; hard max 64",
                networks = results,
            });
        }

        private BridgeResponse InspectNetworkTopology(BridgeRequest request)
        {
            if (!TryGetCity(out _, out BridgeResponse error))
            {
                return error;
            }
            request.Query.TryGetValue("kind", out string kindRaw);
            if (!TypedNetworkMath.TryParseKind(kindRaw, out TypedNetworkKinds filter, out string kindError))
            {
                return BridgeResponse.Error(BridgeErrorKind.InvalidArguments, kindError);
            }

            if (!TryGetOptionalCenter(request, out bool hasCenter, out float2 center, out BridgeResponse centerError))
            {
                return centerError;
            }
            float radius = request.TryGetFloat("radius", out float rawRadius) ? math.max(rawRadius, 1f) : 500f;
            bool includeDeadEnds = request.TryGetBool("include_dead_ends", out bool rawDeadEnds) && rawDeadEnds;

            List<TypedNetworkEdge> snapshot = SnapshotTypedNetworks(
                hasCenter ? (float2?)center : null,
                radius);
            List<NetworkTopologyFinding> issues = filter == TypedNetworkKinds.Road
                ? TypedNetworkMath.FindRoadIssues(snapshot)
                : TypedNetworkMath.FindUtilityIsolatedFindings(snapshot, filter);
            var findings = new List<object>(math.min(issues.Count, kNetworkFindingCap));
            int omitted = 0;
            for (int i = 0; i < issues.Count; i++)
            {
                if (findings.Count >= kNetworkFindingCap)
                {
                    omitted = issues.Count - i;
                    break;
                }
                NetworkTopologyFinding issue = issues[i];
                findings.Add(new
                {
                    type = TypedNetworkMath.TopologyClassName(issue.Class),
                    edges = TopologyEdgeRefs(snapshot, issue.EdgeA, issue.EdgeB),
                    nodes = TopologyNodeRefs(issue.NodeA, issue.NodeB),
                    componentSize = issue.ComponentSize > 0 ? (int?)issue.ComponentSize : null,
                    distanceM = (float)Math.Round(issue.DistanceM, 1),
                });
            }

            object deadEnds = null;
            if (includeDeadEnds && filter == TypedNetworkKinds.Road)
            {
                List<NetworkDeadEnd> facts = TypedNetworkMath.FindRoadDeadEnds(snapshot);
                var listed = new List<object>(math.min(facts.Count, kNetworkFindingCap));
                foreach (NetworkDeadEnd fact in facts)
                {
                    if (listed.Count >= kNetworkFindingCap)
                    {
                        break;
                    }
                    TypedNetworkEdge edge = snapshot[fact.Edge];
                    listed.Add(new
                    {
                        entity = new { index = edge.EntityIndex, version = edge.EntityVersion },
                        node = fact.Node,
                        degree = fact.Degree,
                    });
                }
                deadEnds = listed;
            }

            string note = filter == TypedNetworkKinds.Road
                ? "degree-1 dead ends are facts, not automatic errors; near-miss, unnoded crossing, too-close junctions, short stubs and isolated roads are the QA classes"
                : "utility QA reports isolated components that do not share a node with any road edge";
            return BridgeResponse.Json(new
            {
                kind = TypedNetworkMath.PrimaryKindName(filter),
                returned = findings.Count,
                truncated = omitted > 0,
                warning = omitted > 0
                    ? $"{omitted} additional findings omitted; shrink radius."
                    : null,
                note,
                findings,
                deadEnds,
            });
        }

        private static bool TryGetOptionalCenter(
            BridgeRequest request,
            out bool hasCenter,
            out float2 center,
            out BridgeResponse error)
        {
            hasCenter = false;
            center = default;
            error = null;
            bool hasX = request.TryGetFloat("x", out float x);
            bool hasZ = request.TryGetFloat("z", out float z);
            if (hasX != hasZ)
            {
                error = BridgeResponse.Error(
                    BridgeErrorKind.InvalidArguments,
                    "x and z must both be provided");
                return false;
            }
            if (!hasX)
            {
                return true;
            }
            hasCenter = true;
            center = new float2(x, z);
            return true;
        }

        private static object BuildNetworkListItem(
            TypedNetworkEdge edge,
            string prefabName,
            TypedNetworkKinds filter,
            bool hasCenter,
            float distance,
            float? widthM,
            object traffic)
        {
            var entity = new { index = edge.EntityIndex, version = edge.EntityVersion };
            var start = new { x = edge.Points[0].x, z = edge.Points[0].z };
            var end = new
            {
                x = edge.Points[edge.Points.Length - 1].x,
                z = edge.Points[edge.Points.Length - 1].z,
            };
            double? distanceM = hasCenter ? (double?)Math.Round(distance, 1) : null;
            if (filter == TypedNetworkKinds.Road)
            {
                return new
                {
                    entity,
                    prefab = prefabName,
                    start,
                    end,
                    length = edge.Length,
                    widthM,
                    distanceM,
                    traffic,
                };
            }
            return new
            {
                entity,
                prefab = prefabName,
                start,
                end,
                length = edge.Length,
                distanceM,
            };
        }

        private object ReadRoadTraffic(Entity entity, out float volumeIndex, out float congestionIndex)
        {
            volumeIndex = 0f;
            congestionIndex = 0f;
            if (!EntityManager.HasComponent<Game.Net.Road>(entity))
            {
                return null;
            }
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
            return new
            {
                volumeIndex = (float)Math.Round(volumeIndex, 1),
                congestionIndex = (float)Math.Round(congestionIndex, 1),
                activeBottlenecks,
            };
        }

        private List<TypedNetworkEdge> SnapshotTypedNetworks(float2? center, float radius)
        {
            var snapshot = new List<TypedNetworkEdge>();
            using (NativeArray<Entity> entities = PlacedRoadQuery.ToEntityArray(Allocator.Temp))
            {
                foreach (Entity entity in entities)
                {
                    PrefabRef prefabRef = EntityManager.GetComponentData<PrefabRef>(entity);
                    TypedNetworkKinds kinds = ClassifyNetPrefab(prefabRef.m_Prefab);
                    if ((kinds & TypedNetworkMath.ProductKinds) == TypedNetworkKinds.None)
                    {
                        continue;
                    }
                    Game.Net.Curve curve = EntityManager.GetComponentData<Game.Net.Curve>(entity);
                    float2 midpoint = (curve.m_Bezier.a.xz + curve.m_Bezier.d.xz) * 0.5f;
                    if (center.HasValue && math.distance(midpoint, center.Value) > radius)
                    {
                        continue;
                    }
                    Game.Net.Edge edge = EntityManager.GetComponentData<Game.Net.Edge>(entity);
                    snapshot.Add(new TypedNetworkEdge(
                        entity.Index,
                        entity.Version,
                        edge.m_Start.Index,
                        edge.m_End.Index,
                        SamplePlacementPath(curve),
                        curve.m_Length,
                        kinds,
                        IsOutsideConnectionNode(edge.m_Start),
                        IsOutsideConnectionNode(edge.m_End)));
                }
            }
            return snapshot;
        }

        private TypedNetworkKinds ClassifyNetPrefab(Entity prefabEntity)
        {
            if (prefabEntity == Entity.Null || !EntityManager.Exists(prefabEntity))
            {
                return TypedNetworkKinds.None;
            }
            bool isRoad = EntityManager.HasComponent<RoadData>(prefabEntity);
            bool water = false;
            bool sewage = false;
            bool lowVoltage = false;
            if (EntityManager.HasComponent<NetData>(prefabEntity))
            {
                Game.Net.Layer layers =
                    EntityManager.GetComponentData<NetData>(prefabEntity).m_RequiredLayers;
                water = (layers & Game.Net.Layer.WaterPipe) != 0;
                sewage = (layers & Game.Net.Layer.SewagePipe) != 0;
                lowVoltage = (layers & Game.Net.Layer.PowerlineLow) != 0;
            }
            if (EntityManager.HasComponent<WaterPipeConnectionData>(prefabEntity))
            {
                WaterPipeConnectionData pipe =
                    EntityManager.GetComponentData<WaterPipeConnectionData>(prefabEntity);
                water |= pipe.m_FreshCapacity > 0;
                sewage |= pipe.m_SewageCapacity > 0;
            }
            if (EntityManager.HasComponent<ElectricityConnectionData>(prefabEntity))
            {
                lowVoltage |= EntityManager.GetComponentData<ElectricityConnectionData>(prefabEntity)
                    .m_Voltage == ElectricityConnection.Voltage.Low;
            }
            return TypedNetworkMath.Classify(isRoad, water, sewage, lowVoltage);
        }

        private static TypedNetworkKinds ToTypedNetworkKinds(Game.Net.UtilityTypes utilityTypes)
        {
            return TypedNetworkMath.FromUtilities(
                (utilityTypes & Game.Net.UtilityTypes.WaterPipe) != 0,
                (utilityTypes & Game.Net.UtilityTypes.SewagePipe) != 0,
                (utilityTypes & Game.Net.UtilityTypes.LowVoltageLine) != 0);
        }

        private bool IsOutsideConnectionNode(Entity node)
        {
            return EntityManager.Exists(node)
                && EntityManager.HasComponent<Game.Objects.OutsideConnection>(node);
        }

        private static object[] TopologyEdgeRefs(
            IReadOnlyList<TypedNetworkEdge> snapshot,
            int first,
            int second)
        {
            var refs = new List<object>(2);
            AddEdgeRef(refs, snapshot, first);
            AddEdgeRef(refs, snapshot, second);
            return refs.ToArray();
        }

        private static void AddEdgeRef(
            List<object> refs,
            IReadOnlyList<TypedNetworkEdge> snapshot,
            int index)
        {
            if (index < 0 || index >= snapshot.Count)
            {
                return;
            }
            TypedNetworkEdge edge = snapshot[index];
            refs.Add(new { index = edge.EntityIndex, version = edge.EntityVersion });
        }

        private static object[] TopologyNodeRefs(int first, int second)
        {
            var refs = new List<object>(2);
            if (first >= 0)
            {
                refs.Add(new { index = first });
            }
            if (second >= 0)
            {
                refs.Add(new { index = second });
            }
            return refs.ToArray();
        }
    }
}
