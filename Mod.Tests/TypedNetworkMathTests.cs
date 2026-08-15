using System.Collections.Generic;
using Unity.Mathematics;
using Xunit;

namespace CS2MCP
{
    public sealed class TypedNetworkMathTests
    {
        [Fact]
        public void Road_classification_does_not_absorb_carried_utilities()
        {
            Assert.Equal(
                TypedNetworkKinds.Road,
                TypedNetworkMath.Classify(true, true, true, true));
            Assert.Equal(
                TypedNetworkKinds.Water | TypedNetworkKinds.Sewage,
                TypedNetworkMath.Classify(false, true, true, false));
        }

        [Fact]
        public void Isolated_pipe_does_not_share_a_node_with_a_road()
        {
            TypedNetworkEdge[] edges =
            {
                Road(1, 10, 11, Xz(0f, 0f), Xz(20f, 0f), outsideEnd: true),
                Pipe(2, 20, 21, Xz(100f, 100f), Xz(110f, 100f)),
            };

            bool[] isolated = TypedNetworkMath.IsolatedFlags(edges);

            Assert.False(isolated[0]);
            Assert.True(isolated[1]);
        }

        [Fact]
        public void Pipe_sharing_a_road_node_is_not_isolated()
        {
            TypedNetworkEdge[] edges =
            {
                Road(1, 10, 11, Xz(0f, 0f), Xz(20f, 0f), outsideEnd: true),
                Pipe(2, 11, 21, Xz(20f, 0f), Xz(20f, 15f)),
            };

            bool[] isolated = TypedNetworkMath.IsolatedFlags(edges);

            Assert.False(isolated[0]);
            Assert.False(isolated[1]);
        }

        [Fact]
        public void Road_without_outside_connection_is_an_isolated_component()
        {
            TypedNetworkEdge[] edges =
            {
                Road(1, 10, 11, Xz(0f, 0f), Xz(20f, 0f)),
                Road(2, 11, 12, Xz(20f, 0f), Xz(40f, 0f)),
            };

            List<NetworkTopologyFinding> findings = TypedNetworkMath.FindRoadIssues(edges);

            Assert.Contains(findings, f => f.Class == NetworkTopologyClass.IsolatedRoad && f.ComponentSize == 2);
        }

        [Fact]
        public void Road_with_outside_connection_is_not_isolated()
        {
            TypedNetworkEdge[] edges =
            {
                Road(1, 10, 11, Xz(0f, 0f), Xz(20f, 0f), outsideEnd: true),
            };

            List<NetworkTopologyFinding> findings = TypedNetworkMath.FindRoadIssues(edges);

            Assert.DoesNotContain(findings, f => f.Class == NetworkTopologyClass.IsolatedRoad);
        }

        [Fact]
        public void Near_miss_is_a_finding_and_a_far_dead_end_is_not()
        {
            TypedNetworkEdge[] edges =
            {
                Road(1, 10, 11, Xz(0f, 0f), Xz(20f, 0f), outsideStart: true),
                Road(2, 20, 21, Xz(24f, 4f), Xz(40f, 4f)),
                Road(3, 30, 31, Xz(0f, 80f), Xz(20f, 80f)),
            };

            List<NetworkTopologyFinding> findings = TypedNetworkMath.FindRoadIssues(edges);

            Assert.Contains(findings, f => f.Class == NetworkTopologyClass.NearMiss);
            List<NetworkDeadEnd> deadEnds = TypedNetworkMath.FindRoadDeadEnds(edges);
            Assert.True(deadEnds.Count >= 3);
            Assert.DoesNotContain(
                findings,
                f => f.Class == NetworkTopologyClass.NearMiss && (f.EdgeA == 2 || f.EdgeB == 2));
        }

        [Fact]
        public void Unnoded_crossing_is_reported_and_a_shared_node_is_not()
        {
            TypedNetworkEdge[] crossing =
            {
                Road(1, 10, 11, Xz(0f, 0f), Xz(20f, 20f), outsideStart: true),
                Road(2, 20, 21, Xz(0f, 20f), Xz(20f, 0f), outsideStart: true),
            };
            TypedNetworkEdge[] noded =
            {
                Road(1, 10, 12, Xz(0f, 0f), Xz(10f, 10f), outsideStart: true),
                Road(2, 12, 21, Xz(10f, 10f), Xz(20f, 0f), outsideEnd: true),
                Road(3, 20, 12, Xz(0f, 20f), Xz(10f, 10f)),
            };

            Assert.Contains(
                TypedNetworkMath.FindRoadIssues(crossing),
                f => f.Class == NetworkTopologyClass.UnnodedCrossing);
            Assert.DoesNotContain(
                TypedNetworkMath.FindRoadIssues(noded),
                f => f.Class == NetworkTopologyClass.UnnodedCrossing);
        }

        [Fact]
        public void Too_close_junctions_are_reported()
        {
            TypedNetworkEdge[] edges =
            {
                Road(1, 1, 10, Xz(0f, 0f), Xz(-20f, 0f), outsideEnd: true),
                Road(2, 1, 11, Xz(0f, 0f), Xz(0f, 20f)),
                Road(3, 1, 12, Xz(0f, 0f), Xz(0f, -20f)),
                Road(4, 2, 20, Xz(5f, 0f), Xz(25f, 0f), outsideEnd: true),
                Road(5, 2, 21, Xz(5f, 0f), Xz(5f, 20f)),
                Road(6, 2, 22, Xz(5f, 0f), Xz(5f, -20f)),
            };

            Assert.Contains(
                TypedNetworkMath.FindRoadIssues(edges),
                f => f.Class == NetworkTopologyClass.TooCloseJunctions && f.DistanceM < 12f);
        }

        [Fact]
        public void Short_stub_is_a_finding_and_a_long_dead_end_is_not()
        {
            TypedNetworkEdge[] edges =
            {
                Road(1, 10, 11, Xz(0f, 0f), Xz(40f, 0f), outsideStart: true),
                Road(2, 11, 12, Xz(40f, 0f), Xz(44f, 0f), length: 4f),
                Road(3, 10, 13, Xz(0f, 0f), Xz(0f, 30f), length: 30f),
            };

            List<NetworkTopologyFinding> findings = TypedNetworkMath.FindRoadIssues(edges);
            Assert.Contains(findings, f => f.Class == NetworkTopologyClass.ShortStub && f.EdgeA == 1);
            Assert.DoesNotContain(findings, f => f.Class == NetworkTopologyClass.ShortStub && f.EdgeA == 2);
        }

        [Fact]
        public void Isolated_pipe_findings_are_one_per_component()
        {
            TypedNetworkEdge[] edges =
            {
                Road(1, 10, 11, Xz(0f, 0f), Xz(20f, 0f), outsideEnd: true),
                Pipe(2, 20, 21, Xz(100f, 100f), Xz(110f, 100f)),
                Pipe(3, 21, 22, Xz(110f, 100f), Xz(120f, 100f)),
                Cable(4, 30, 31, Xz(200f, 200f), Xz(210f, 200f)),
            };

            List<NetworkTopologyFinding> water =
                TypedNetworkMath.FindUtilityIsolatedFindings(edges, TypedNetworkKinds.Water);
            List<NetworkTopologyFinding> cables =
                TypedNetworkMath.FindUtilityIsolatedFindings(edges, TypedNetworkKinds.LowVoltage);

            Assert.Contains(
                water,
                f => f.Class == NetworkTopologyClass.IsolatedWater && f.ComponentSize == 2);
            Assert.Contains(
                cables,
                f => f.Class == NetworkTopologyClass.IsolatedLowVoltage && f.ComponentSize == 1);
            Assert.Empty(
                TypedNetworkMath.FindUtilityIsolatedFindings(edges, TypedNetworkKinds.Road));
        }

        [Fact]
        public void Pipe_sharing_a_road_node_is_not_an_isolated_finding()
        {
            TypedNetworkEdge[] edges =
            {
                Road(1, 10, 11, Xz(0f, 0f), Xz(20f, 0f), outsideEnd: true),
                Pipe(2, 11, 21, Xz(20f, 0f), Xz(20f, 15f)),
            };

            Assert.Empty(
                TypedNetworkMath.FindUtilityIsolatedFindings(edges, TypedNetworkKinds.Water));
        }

        [Fact]
        public void Kind_filter_parsing_requires_a_single_kind()
        {
            Assert.False(TypedNetworkMath.TryParseKind(null, out _, out string missing));
            Assert.Contains("required", missing);
            Assert.False(TypedNetworkMath.TryParseKind("", out _, out string empty));
            Assert.Contains("required", empty);
            Assert.True(TypedNetworkMath.TryParseKind("road", out TypedNetworkKinds road, out _));
            Assert.Equal(TypedNetworkKinds.Road, road);
            Assert.False(TypedNetworkMath.TryParseKind("pipe", out _, out string error));
            Assert.Contains("low_voltage", error);
            Assert.False(TypedNetworkMath.TryParseKind("all", out _, out string all));
            Assert.Contains("low_voltage", all);
        }

        [Fact]
        public void Network_sort_parsing_is_kind_aware()
        {
            Assert.True(
                TypedNetworkMath.TryParseNetworkSort(
                    null,
                    TypedNetworkKinds.Road,
                    false,
                    out string empty,
                    out _));
            Assert.Null(empty);
            Assert.True(
                TypedNetworkMath.TryParseNetworkSort(
                    "traffic_volume",
                    TypedNetworkKinds.Road,
                    false,
                    out string volume,
                    out _));
            Assert.Equal("traffic_volume", volume);
            Assert.False(
                TypedNetworkMath.TryParseNetworkSort(
                    "traffic_volume",
                    TypedNetworkKinds.Water,
                    false,
                    out _,
                    out string waterTraffic));
            Assert.Contains("kind=road", waterTraffic);
            Assert.False(
                TypedNetworkMath.TryParseNetworkSort(
                    "load",
                    TypedNetworkKinds.Road,
                    false,
                    out _,
                    out string roadLoad));
            Assert.Contains("low_voltage", roadLoad);
            Assert.True(
                TypedNetworkMath.TryParseNetworkSort(
                    "load",
                    TypedNetworkKinds.LowVoltage,
                    false,
                    out string load,
                    out _));
            Assert.Equal("load", load);
            Assert.False(
                TypedNetworkMath.TryParseNetworkSort(
                    "distance",
                    TypedNetworkKinds.Sewage,
                    false,
                    out _,
                    out string distanceError));
            Assert.Contains("x and z", distanceError);
            Assert.False(
                TypedNetworkMath.TryParseNetworkSort(
                    "lanes",
                    TypedNetworkKinds.Road,
                    true,
                    out _,
                    out string unknown));
            Assert.Contains("congestion", unknown);
        }

        [Fact]
        public void Traffic_sort_ranks_highest_first_and_distance_ranks_nearest()
        {
            Assert.True(
                TypedNetworkMath.NetworkListRank("traffic_volume", 10f, 80f, 5f)
                < TypedNetworkMath.NetworkListRank("traffic_volume", 1f, 10f, 90f));
            Assert.True(
                TypedNetworkMath.NetworkListRank("congestion", 10f, 10f, 40f)
                < TypedNetworkMath.NetworkListRank("congestion", 1f, 80f, 5f));
            Assert.True(
                TypedNetworkMath.NetworkListRank("load", 10f, 0f, 0f, 0.9f)
                < TypedNetworkMath.NetworkListRank("load", 1f, 0f, 0f, 0.2f));
            Assert.True(
                TypedNetworkMath.NetworkListRank("distance", 3f, 0f, 0f)
                < TypedNetworkMath.NetworkListRank("distance", 9f, 100f, 100f));
        }

        [Fact]
        public void Electricity_load_uses_absolute_flow_and_keeps_the_worst_edge()
        {
            Assert.Equal(0f, TypedNetworkMath.ElectricityLoadRatio(10, 0));
            Assert.Equal(0.5f, TypedNetworkMath.ElectricityLoadRatio(-10, 20), 5);

            int flow = 0;
            int capacity = 0;
            bool bottleneck = false;
            float load = -1f;
            TypedNetworkMath.ConsiderElectricityEdge(4, 20, false, ref flow, ref capacity, ref bottleneck, ref load);
            TypedNetworkMath.ConsiderElectricityEdge(-18, 20, true, ref flow, ref capacity, ref bottleneck, ref load);
            TypedNetworkMath.ConsiderElectricityEdge(5, 5, false, ref flow, ref capacity, ref bottleneck, ref load);
            Assert.Equal(5, flow);
            Assert.Equal(5, capacity);
            Assert.True(bottleneck);
            Assert.Equal(1f, load, 5);
        }

        private static TypedNetworkEdge Road(
            int id,
            int start,
            int end,
            float3 a,
            float3 b,
            bool outsideStart = false,
            bool outsideEnd = false,
            float length = -1f)
        {
            return new TypedNetworkEdge(
                id,
                1,
                start,
                end,
                new[] { a, b },
                length >= 0f ? length : math.distance(a.xz, b.xz),
                TypedNetworkKinds.Road,
                outsideStart,
                outsideEnd);
        }

        private static TypedNetworkEdge Pipe(int id, int start, int end, float3 a, float3 b)
        {
            return new TypedNetworkEdge(
                id,
                1,
                start,
                end,
                new[] { a, b },
                math.distance(a.xz, b.xz),
                TypedNetworkKinds.Water,
                false,
                false);
        }

        private static TypedNetworkEdge Cable(int id, int start, int end, float3 a, float3 b)
        {
            return new TypedNetworkEdge(
                id,
                1,
                start,
                end,
                new[] { a, b },
                math.distance(a.xz, b.xz),
                TypedNetworkKinds.LowVoltage,
                false,
                false);
        }

        private static float3 Xz(float x, float z)
        {
            return new float3(x, 0f, z);
        }
    }
}
