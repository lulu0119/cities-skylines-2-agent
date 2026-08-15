using System;
using System.Collections.Generic;
using Unity.Mathematics;

namespace CS2MCP
{
    [Flags]
    internal enum TypedNetworkKinds : byte
    {
        None = 0,
        Water = 1,
        Sewage = 2,
        LowVoltage = 4,
        Road = 8,
    }

    internal enum NetworkTopologyClass : byte
    {
        NearMiss,
        UnnodedCrossing,
        TooCloseJunctions,
        ShortStub,
        IsolatedRoad,
        IsolatedWater,
        IsolatedSewage,
        IsolatedLowVoltage,
    }

    /// <summary>
    /// One top-level net edge in a typed snapshot. Geometry is sampled world
    /// points; classification is already resolved by the caller.
    /// </summary>
    internal readonly struct TypedNetworkEdge
    {
        public TypedNetworkEdge(
            int entityIndex,
            int entityVersion,
            int startNode,
            int endNode,
            float3[] points,
            float length,
            TypedNetworkKinds kinds,
            bool startOutside,
            bool endOutside)
        {
            EntityIndex = entityIndex;
            EntityVersion = entityVersion;
            StartNode = startNode;
            EndNode = endNode;
            Points = points;
            Length = length;
            Kinds = kinds;
            StartOutside = startOutside;
            EndOutside = endOutside;
        }

        public int EntityIndex { get; }
        public int EntityVersion { get; }
        public int StartNode { get; }
        public int EndNode { get; }
        public float3[] Points { get; }
        public float Length { get; }
        public TypedNetworkKinds Kinds { get; }
        public bool StartOutside { get; }
        public bool EndOutside { get; }
    }

    internal readonly struct NetworkTopologyFinding
    {
        public NetworkTopologyFinding(
            NetworkTopologyClass topologyClass,
            int edgeA,
            int edgeB,
            int nodeA,
            int nodeB,
            int componentSize,
            float distanceM)
        {
            Class = topologyClass;
            EdgeA = edgeA;
            EdgeB = edgeB;
            NodeA = nodeA;
            NodeB = nodeB;
            ComponentSize = componentSize;
            DistanceM = distanceM;
        }

        public NetworkTopologyClass Class { get; }
        public int EdgeA { get; }
        public int EdgeB { get; }
        public int NodeA { get; }
        public int NodeB { get; }
        public int ComponentSize { get; }
        public float DistanceM { get; }
    }

    internal readonly struct NetworkDeadEnd
    {
        public NetworkDeadEnd(int edge, int node, int degree)
        {
            Edge = edge;
            Node = node;
            Degree = degree;
        }

        public int Edge { get; }
        public int Node { get; }
        public int Degree { get; }
    }

    /// <summary>
    /// Typed-network identity, connected components, and road topology QA.
    /// Callers map native prefab layers onto <see cref="TypedNetworkKinds"/>;
    /// this module never sees ECS.
    /// </summary>
    internal static class TypedNetworkMath
    {
        public const TypedNetworkKinds ProductKinds =
            TypedNetworkKinds.Road
            | TypedNetworkKinds.Water
            | TypedNetworkKinds.Sewage
            | TypedNetworkKinds.LowVoltage;

        public const float NearMissMeters = 16f;
        public const float ShortStubMeters = 8f;
        public const float CloseJunctionMeters = 12f;
        public const float CrossingHeightMeters = 4f;
        public const float GridMeters = 96f;

        public static TypedNetworkKinds FromUtilities(bool water, bool sewage, bool lowVoltage)
        {
            TypedNetworkKinds kinds = TypedNetworkKinds.None;
            if (water)
            {
                kinds |= TypedNetworkKinds.Water;
            }
            if (sewage)
            {
                kinds |= TypedNetworkKinds.Sewage;
            }
            if (lowVoltage)
            {
                kinds |= TypedNetworkKinds.LowVoltage;
            }
            return kinds;
        }

        public static TypedNetworkKinds Classify(bool isRoad, bool water, bool sewage, bool lowVoltage)
        {
            if (isRoad)
            {
                return TypedNetworkKinds.Road;
            }
            return FromUtilities(water, sewage, lowVoltage);
        }

        public static bool MatchesFilter(TypedNetworkKinds kinds, TypedNetworkKinds filter)
        {
            if ((kinds & ProductKinds) == TypedNetworkKinds.None)
            {
                return false;
            }
            return filter == ProductKinds || (kinds & filter) != 0;
        }

        public static bool TryParseKind(string raw, out TypedNetworkKinds kind, out string error)
        {
            kind = TypedNetworkKinds.None;
            error = null;
            if (string.IsNullOrWhiteSpace(raw))
            {
                error = "kind is required; pass road, water, sewage or low_voltage";
                return false;
            }
            switch (raw.Trim().ToLowerInvariant())
            {
                case "road":
                    kind = TypedNetworkKinds.Road;
                    return true;
                case "water":
                    kind = TypedNetworkKinds.Water;
                    return true;
                case "sewage":
                    kind = TypedNetworkKinds.Sewage;
                    return true;
                case "low_voltage":
                    kind = TypedNetworkKinds.LowVoltage;
                    return true;
                default:
                    error = "kind must be road, water, sewage or low_voltage";
                    return false;
            }
        }

        public static bool TryParseNetworkSort(
            string raw,
            TypedNetworkKinds kind,
            bool hasCenter,
            out string sort,
            out string error)
        {
            sort = string.IsNullOrWhiteSpace(raw) ? null : raw.Trim().ToLowerInvariant();
            error = null;
            if (string.IsNullOrEmpty(sort))
            {
                return true;
            }
            if (sort == "distance")
            {
                if (!hasCenter)
                {
                    error = "sort=distance requires both x and z";
                    return false;
                }
                return true;
            }
            if (sort == "traffic_volume" || sort == "congestion")
            {
                if (kind != TypedNetworkKinds.Road)
                {
                    error = "sort=traffic_volume and sort=congestion are only valid for kind=road";
                    return false;
                }
                return true;
            }
            if (sort == "load")
            {
                if (kind != TypedNetworkKinds.LowVoltage)
                {
                    error = "sort=load is only valid for kind=low_voltage";
                    return false;
                }
                return true;
            }
            if (kind == TypedNetworkKinds.Road)
            {
                error = "sort must be distance, traffic_volume or congestion";
            }
            else if (kind == TypedNetworkKinds.LowVoltage)
            {
                error = "sort must be distance or load";
            }
            else
            {
                error = "sort must be distance for water and sewage";
            }
            return false;
        }

        public static float NetworkListRank(
            string sort,
            float distance,
            float volumeIndex,
            float congestionIndex,
            float loadRatio = 0f)
        {
            if (sort == "traffic_volume")
            {
                return -volumeIndex;
            }
            if (sort == "congestion")
            {
                return -congestionIndex;
            }
            if (sort == "load")
            {
                return -loadRatio;
            }
            return distance;
        }

        public static float ElectricityLoadRatio(int flow, int capacity)
        {
            if (capacity <= 0)
            {
                return 0f;
            }
            return math.abs(flow) / (float)capacity;
        }

        /// <summary>
        /// Keep the worst-loaded incident flow edge. Bottleneck stays true
        /// once any incident edge is a bottleneck.
        /// </summary>
        public static void ConsiderElectricityEdge(
            int flow,
            int capacity,
            bool bottleneck,
            ref int chosenAbsFlow,
            ref int chosenCapacity,
            ref bool anyBottleneck,
            ref float chosenLoad)
        {
            anyBottleneck |= bottleneck;
            int absFlow = math.abs(flow);
            float load = ElectricityLoadRatio(flow, capacity);
            if (load < chosenLoad || (load == chosenLoad && absFlow <= chosenAbsFlow))
            {
                return;
            }
            chosenLoad = load;
            chosenAbsFlow = absFlow;
            chosenCapacity = capacity;
        }

        public static string PrimaryKindName(TypedNetworkKinds kinds)
        {
            if ((kinds & TypedNetworkKinds.Road) != 0)
            {
                return "road";
            }
            if ((kinds & TypedNetworkKinds.Water) != 0)
            {
                return "water";
            }
            if ((kinds & TypedNetworkKinds.Sewage) != 0)
            {
                return "sewage";
            }
            if ((kinds & TypedNetworkKinds.LowVoltage) != 0)
            {
                return "low_voltage";
            }
            return "none";
        }

        public static string TopologyClassName(NetworkTopologyClass topologyClass)
        {
            switch (topologyClass)
            {
                case NetworkTopologyClass.NearMiss:
                    return "near_miss";
                case NetworkTopologyClass.UnnodedCrossing:
                    return "unnoded_crossing";
                case NetworkTopologyClass.TooCloseJunctions:
                    return "too_close_junctions";
                case NetworkTopologyClass.ShortStub:
                    return "short_stub";
                case NetworkTopologyClass.IsolatedRoad:
                    return "isolated_road";
                case NetworkTopologyClass.IsolatedWater:
                    return "isolated_water";
                case NetworkTopologyClass.IsolatedSewage:
                    return "isolated_sewage";
                case NetworkTopologyClass.IsolatedLowVoltage:
                    return "isolated_low_voltage";
                default:
                    return "unknown";
            }
        }

        public static int[] LabelComponents(
            IReadOnlyList<TypedNetworkEdge> edges,
            TypedNetworkKinds kind)
        {
            int count = edges != null ? edges.Count : 0;
            var labels = new int[count];
            var union = new Dictionary<int, int>();
            for (int i = 0; i < count; i++)
            {
                TypedNetworkEdge edge = edges[i];
                if ((edge.Kinds & kind) == 0)
                {
                    labels[i] = -1;
                    continue;
                }
                Union(union, edge.StartNode, edge.EndNode);
            }
            for (int i = 0; i < count; i++)
            {
                if (labels[i] == -1)
                {
                    continue;
                }
                labels[i] = Find(union, edges[i].StartNode);
            }
            return labels;
        }

        public static bool ComponentIsIsolated(
            IReadOnlyList<TypedNetworkEdge> edges,
            TypedNetworkKinds kind,
            int componentId,
            int[] labels)
        {
            if (edges == null || labels == null || componentId < 0)
            {
                return false;
            }

            var nodes = new HashSet<int>();
            bool hasOutside = false;
            for (int i = 0; i < edges.Count; i++)
            {
                if (labels[i] != componentId)
                {
                    continue;
                }
                TypedNetworkEdge edge = edges[i];
                nodes.Add(edge.StartNode);
                nodes.Add(edge.EndNode);
                if (edge.StartOutside || edge.EndOutside)
                {
                    hasOutside = true;
                }
            }
            if (nodes.Count == 0)
            {
                return false;
            }
            if (kind == TypedNetworkKinds.Road)
            {
                return !hasOutside;
            }
            for (int i = 0; i < edges.Count; i++)
            {
                TypedNetworkEdge edge = edges[i];
                if ((edge.Kinds & TypedNetworkKinds.Road) == 0)
                {
                    continue;
                }
                if (nodes.Contains(edge.StartNode) || nodes.Contains(edge.EndNode))
                {
                    return false;
                }
            }
            return true;
        }

        public static bool[] IsolatedFlags(IReadOnlyList<TypedNetworkEdge> edges)
        {
            int count = edges != null ? edges.Count : 0;
            var flags = new bool[count];
            if (count == 0)
            {
                return flags;
            }

            int[] roadLabels = LabelComponents(edges, TypedNetworkKinds.Road);
            int[] waterLabels = LabelComponents(edges, TypedNetworkKinds.Water);
            int[] sewageLabels = LabelComponents(edges, TypedNetworkKinds.Sewage);
            int[] lowVoltageLabels = LabelComponents(edges, TypedNetworkKinds.LowVoltage);
            for (int i = 0; i < count; i++)
            {
                TypedNetworkEdge edge = edges[i];
                if ((edge.Kinds & TypedNetworkKinds.Road) != 0)
                {
                    flags[i] = ComponentIsIsolated(
                        edges,
                        TypedNetworkKinds.Road,
                        roadLabels[i],
                        roadLabels);
                    continue;
                }
                bool isolated = true;
                int counted = 0;
                if ((edge.Kinds & TypedNetworkKinds.Water) != 0)
                {
                    counted++;
                    isolated &= ComponentIsIsolated(
                        edges,
                        TypedNetworkKinds.Water,
                        waterLabels[i],
                        waterLabels);
                }
                if ((edge.Kinds & TypedNetworkKinds.Sewage) != 0)
                {
                    counted++;
                    isolated &= ComponentIsIsolated(
                        edges,
                        TypedNetworkKinds.Sewage,
                        sewageLabels[i],
                        sewageLabels);
                }
                if ((edge.Kinds & TypedNetworkKinds.LowVoltage) != 0)
                {
                    counted++;
                    isolated &= ComponentIsIsolated(
                        edges,
                        TypedNetworkKinds.LowVoltage,
                        lowVoltageLabels[i],
                        lowVoltageLabels);
                }
                flags[i] = counted > 0 && isolated;
            }
            return flags;
        }

        public static int ComponentSize(int[] labels, int componentId)
        {
            if (labels == null || componentId < 0)
            {
                return 0;
            }
            int size = 0;
            for (int i = 0; i < labels.Length; i++)
            {
                if (labels[i] == componentId)
                {
                    size++;
                }
            }
            return size;
        }

        public static Dictionary<int, int> RoadDegrees(IReadOnlyList<TypedNetworkEdge> edges)
        {
            var degrees = new Dictionary<int, int>();
            if (edges == null)
            {
                return degrees;
            }
            for (int i = 0; i < edges.Count; i++)
            {
                TypedNetworkEdge edge = edges[i];
                if ((edge.Kinds & TypedNetworkKinds.Road) == 0)
                {
                    continue;
                }
                AddDegree(degrees, edge.StartNode);
                AddDegree(degrees, edge.EndNode);
            }
            return degrees;
        }

        public static List<NetworkDeadEnd> FindRoadDeadEnds(IReadOnlyList<TypedNetworkEdge> edges)
        {
            var deadEnds = new List<NetworkDeadEnd>();
            Dictionary<int, int> degrees = RoadDegrees(edges);
            if (edges == null)
            {
                return deadEnds;
            }
            for (int i = 0; i < edges.Count; i++)
            {
                TypedNetworkEdge edge = edges[i];
                if ((edge.Kinds & TypedNetworkKinds.Road) == 0)
                {
                    continue;
                }
                if (DegreeOf(degrees, edge.StartNode) == 1)
                {
                    deadEnds.Add(new NetworkDeadEnd(i, edge.StartNode, 1));
                }
                if (DegreeOf(degrees, edge.EndNode) == 1)
                {
                    deadEnds.Add(new NetworkDeadEnd(i, edge.EndNode, 1));
                }
            }
            return deadEnds;
        }

        public static List<NetworkTopologyFinding> FindRoadIssues(IReadOnlyList<TypedNetworkEdge> edges)
        {
            var findings = new List<NetworkTopologyFinding>();
            if (edges == null || edges.Count == 0)
            {
                return findings;
            }

            Dictionary<int, int> degrees = RoadDegrees(edges);
            AddIsolatedComponents(
                edges,
                TypedNetworkKinds.Road,
                NetworkTopologyClass.IsolatedRoad,
                findings);
            AddShortStubs(edges, degrees, findings);
            AddTooCloseJunctions(edges, degrees, findings);
            AddNearMisses(edges, degrees, findings);
            AddUnnodedCrossings(edges, findings);
            return findings;
        }

        /// <summary>
        /// One finding per utility component that does not share a node with any
        /// road edge. Snapshot must still include roads so connectivity is visible.
        /// </summary>
        public static List<NetworkTopologyFinding> FindUtilityIsolatedFindings(
            IReadOnlyList<TypedNetworkEdge> edges,
            TypedNetworkKinds kind)
        {
            var findings = new List<NetworkTopologyFinding>();
            NetworkTopologyClass topologyClass;
            if (kind == TypedNetworkKinds.Water)
            {
                topologyClass = NetworkTopologyClass.IsolatedWater;
            }
            else if (kind == TypedNetworkKinds.Sewage)
            {
                topologyClass = NetworkTopologyClass.IsolatedSewage;
            }
            else if (kind == TypedNetworkKinds.LowVoltage)
            {
                topologyClass = NetworkTopologyClass.IsolatedLowVoltage;
            }
            else
            {
                return findings;
            }
            AddIsolatedComponents(edges, kind, topologyClass, findings);
            return findings;
        }

        private static void AddIsolatedComponents(
            IReadOnlyList<TypedNetworkEdge> edges,
            TypedNetworkKinds kind,
            NetworkTopologyClass topologyClass,
            List<NetworkTopologyFinding> findings)
        {
            if (edges == null || edges.Count == 0)
            {
                return;
            }
            int[] labels = LabelComponents(edges, kind);
            var seen = new HashSet<int>();
            for (int i = 0; i < edges.Count; i++)
            {
                int componentId = labels[i];
                if (componentId < 0 || !seen.Add(componentId))
                {
                    continue;
                }
                if (!ComponentIsIsolated(edges, kind, componentId, labels))
                {
                    continue;
                }
                int size = 0;
                int first = i;
                for (int j = 0; j < edges.Count; j++)
                {
                    if (labels[j] == componentId)
                    {
                        if (size == 0)
                        {
                            first = j;
                        }
                        size++;
                    }
                }
                findings.Add(new NetworkTopologyFinding(
                    topologyClass,
                    first,
                    -1,
                    edges[first].StartNode,
                    edges[first].EndNode,
                    size,
                    0f));
            }
        }

        private static void AddShortStubs(
            IReadOnlyList<TypedNetworkEdge> edges,
            Dictionary<int, int> degrees,
            List<NetworkTopologyFinding> findings)
        {
            for (int i = 0; i < edges.Count; i++)
            {
                TypedNetworkEdge edge = edges[i];
                if ((edge.Kinds & TypedNetworkKinds.Road) == 0
                    || edge.Length >= ShortStubMeters)
                {
                    continue;
                }
                int startDegree = DegreeOf(degrees, edge.StartNode);
                int endDegree = DegreeOf(degrees, edge.EndNode);
                if (math.min(startDegree, endDegree) > 1)
                {
                    continue;
                }
                findings.Add(new NetworkTopologyFinding(
                    NetworkTopologyClass.ShortStub,
                    i,
                    -1,
                    startDegree <= endDegree ? edge.StartNode : edge.EndNode,
                    startDegree <= endDegree ? edge.EndNode : edge.StartNode,
                    1,
                    edge.Length));
            }
        }

        private static void AddTooCloseJunctions(
            IReadOnlyList<TypedNetworkEdge> edges,
            Dictionary<int, int> degrees,
            List<NetworkTopologyFinding> findings)
        {
            var junctions = new Dictionary<int, float3>();
            for (int i = 0; i < edges.Count; i++)
            {
                TypedNetworkEdge edge = edges[i];
                if ((edge.Kinds & TypedNetworkKinds.Road) == 0
                    || edge.Points == null
                    || edge.Points.Length < 2)
                {
                    continue;
                }
                if (DegreeOf(degrees, edge.StartNode) >= 3)
                {
                    junctions[edge.StartNode] = edge.Points[0];
                }
                if (DegreeOf(degrees, edge.EndNode) >= 3)
                {
                    junctions[edge.EndNode] = edge.Points[edge.Points.Length - 1];
                }
            }
            var nodes = new List<int>(junctions.Keys);
            for (int i = 0; i < nodes.Count; i++)
            {
                for (int j = i + 1; j < nodes.Count; j++)
                {
                    float distance = math.distance(junctions[nodes[i]].xz, junctions[nodes[j]].xz);
                    if (distance >= CloseJunctionMeters)
                    {
                        continue;
                    }
                    findings.Add(new NetworkTopologyFinding(
                        NetworkTopologyClass.TooCloseJunctions,
                        -1,
                        -1,
                        nodes[i],
                        nodes[j],
                        0,
                        distance));
                }
            }
        }

        private static void AddNearMisses(
            IReadOnlyList<TypedNetworkEdge> edges,
            Dictionary<int, int> degrees,
            List<NetworkTopologyFinding> findings)
        {
            for (int i = 0; i < edges.Count; i++)
            {
                TypedNetworkEdge edge = edges[i];
                if ((edge.Kinds & TypedNetworkKinds.Road) == 0
                    || edge.Points == null
                    || edge.Points.Length < 2)
                {
                    continue;
                }
                TryNearMiss(edges, degrees, i, edge.StartNode, edge.Points[0], findings);
                TryNearMiss(
                    edges,
                    degrees,
                    i,
                    edge.EndNode,
                    edge.Points[edge.Points.Length - 1],
                    findings);
            }
        }

        private static void TryNearMiss(
            IReadOnlyList<TypedNetworkEdge> edges,
            Dictionary<int, int> degrees,
            int edgeIndex,
            int node,
            float3 position,
            List<NetworkTopologyFinding> findings)
        {
            if (DegreeOf(degrees, node) != 1)
            {
                return;
            }
            float best = NearMissMeters * NearMissMeters;
            int bestEdge = -1;
            TypedNetworkEdge self = edges[edgeIndex];
            for (int i = 0; i < edges.Count; i++)
            {
                if (i == edgeIndex)
                {
                    continue;
                }
                TypedNetworkEdge other = edges[i];
                if ((other.Kinds & TypedNetworkKinds.Road) == 0
                    || other.Points == null
                    || other.Points.Length < 2
                    || SharesNode(other, self.StartNode)
                    || SharesNode(other, self.EndNode))
                {
                    continue;
                }
                float distanceSquared = DistanceSquaredToPolyline(position.xz, other.Points);
                if (distanceSquared < best)
                {
                    best = distanceSquared;
                    bestEdge = i;
                }
            }
            if (bestEdge < 0)
            {
                return;
            }
            findings.Add(new NetworkTopologyFinding(
                NetworkTopologyClass.NearMiss,
                edgeIndex,
                bestEdge,
                node,
                -1,
                0,
                math.sqrt(best)));
        }

        private static void AddUnnodedCrossings(
            IReadOnlyList<TypedNetworkEdge> edges,
            List<NetworkTopologyFinding> findings)
        {
            var grid = new Dictionary<long, List<int>>();
            for (int i = 0; i < edges.Count; i++)
            {
                TypedNetworkEdge edge = edges[i];
                if ((edge.Kinds & TypedNetworkKinds.Road) == 0
                    || edge.Points == null
                    || edge.Points.Length < 2)
                {
                    continue;
                }
                PolylineBounds(edge.Points, out float2 boundsMin, out float2 boundsMax);
                int minX = (int)math.floor(boundsMin.x / GridMeters);
                int maxX = (int)math.floor(boundsMax.x / GridMeters);
                int minZ = (int)math.floor(boundsMin.y / GridMeters);
                int maxZ = (int)math.floor(boundsMax.y / GridMeters);
                for (int x = minX; x <= maxX; x++)
                {
                    for (int z = minZ; z <= maxZ; z++)
                    {
                        long key = ((long)x << 32) ^ (uint)z;
                        if (!grid.TryGetValue(key, out List<int> list))
                        {
                            list = new List<int>();
                            grid[key] = list;
                        }
                        if (list.Count == 0 || list[list.Count - 1] != i)
                        {
                            list.Add(i);
                        }
                    }
                }
            }

            var seen = new HashSet<long>();
            foreach (List<int> cell in grid.Values)
            {
                for (int i = 0; i < cell.Count; i++)
                {
                    for (int j = i + 1; j < cell.Count; j++)
                    {
                        int a = cell[i];
                        int b = cell[j];
                        long pair = a < b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a;
                        if (!seen.Add(pair))
                        {
                            continue;
                        }
                        TypedNetworkEdge first = edges[a];
                        TypedNetworkEdge second = edges[b];
                        if (first.StartNode == second.StartNode
                            || first.StartNode == second.EndNode
                            || first.EndNode == second.StartNode
                            || first.EndNode == second.EndNode)
                        {
                            continue;
                        }
                        if (!TryPolylineCrossing(first.Points, second.Points, out float heightDelta))
                        {
                            continue;
                        }
                        if (heightDelta > CrossingHeightMeters)
                        {
                            continue;
                        }
                        findings.Add(new NetworkTopologyFinding(
                            NetworkTopologyClass.UnnodedCrossing,
                            a,
                            b,
                            -1,
                            -1,
                            0,
                            heightDelta));
                    }
                }
            }
        }

        private static float DistanceSquaredToPolyline(float2 point, float3[] points)
        {
            float best = float.MaxValue;
            for (int i = 1; i < points.Length; i++)
            {
                best = math.min(
                    best,
                    PlacementSearchMath.DistanceSquaredToSegment(
                        point,
                        points[i - 1].xz,
                        points[i].xz));
            }
            return best;
        }

        private static bool TryPolylineCrossing(float3[] first, float3[] second, out float heightDelta)
        {
            heightDelta = 0f;
            for (int i = 1; i < first.Length; i++)
            {
                for (int j = 1; j < second.Length; j++)
                {
                    if (!TrySegmentIntersection(
                            first[i - 1].xz,
                            first[i].xz,
                            second[j - 1].xz,
                            second[j].xz,
                            out float t,
                            out float u))
                    {
                        continue;
                    }
                    float y1 = math.lerp(first[i - 1].y, first[i].y, t);
                    float y2 = math.lerp(second[j - 1].y, second[j].y, u);
                    heightDelta = math.abs(y1 - y2);
                    return true;
                }
            }
            return false;
        }

        private static bool TrySegmentIntersection(
            float2 a,
            float2 b,
            float2 c,
            float2 d,
            out float t,
            out float u)
        {
            t = 0f;
            u = 0f;
            float2 r = b - a;
            float2 s = d - c;
            float denom = r.x * s.y - r.y * s.x;
            if (math.abs(denom) < 0.0001f)
            {
                return false;
            }
            float2 ca = c - a;
            t = (ca.x * s.y - ca.y * s.x) / denom;
            u = (ca.x * r.y - ca.y * r.x) / denom;
            const float epsilon = 0.02f;
            return t > epsilon && t < 1f - epsilon && u > epsilon && u < 1f - epsilon;
        }

        private static void PolylineBounds(float3[] points, out float2 min, out float2 max)
        {
            min = points[0].xz;
            max = min;
            for (int i = 1; i < points.Length; i++)
            {
                min = math.min(min, points[i].xz);
                max = math.max(max, points[i].xz);
            }
        }

        private static bool SharesNode(TypedNetworkEdge edge, int node)
        {
            return edge.StartNode == node || edge.EndNode == node;
        }

        private static void AddDegree(Dictionary<int, int> degrees, int node)
        {
            degrees.TryGetValue(node, out int current);
            degrees[node] = current + 1;
        }

        private static int DegreeOf(Dictionary<int, int> degrees, int node)
        {
            return degrees.TryGetValue(node, out int degree) ? degree : 0;
        }

        private static void Union(Dictionary<int, int> parent, int a, int b)
        {
            int rootA = Find(parent, a);
            int rootB = Find(parent, b);
            if (rootA != rootB)
            {
                parent[rootA] = rootB;
            }
        }

        private static int Find(Dictionary<int, int> parent, int node)
        {
            if (!parent.TryGetValue(node, out int next))
            {
                parent[node] = node;
                return node;
            }
            if (next != node)
            {
                next = Find(parent, next);
                parent[node] = next;
            }
            return next;
        }
    }
}
