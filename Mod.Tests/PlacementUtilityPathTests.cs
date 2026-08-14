using Unity.Entities;
using Unity.Mathematics;
using Xunit;

namespace CS2MCP
{
    public sealed class PlacementUtilityPathTests
    {
        [Fact]
        public void LowVoltage_does_not_select_nearer_path_without_low_voltage_lane()
        {
            PlacementUtilityPath[] paths =
            {
                Path(TypedNetworkKinds.Water | TypedNetworkKinds.Sewage, 10f),
                Path(TypedNetworkKinds.LowVoltage, 40f),
            };

            bool found = PlacementSearchMath.TryFindNearestUtilityPoint(
                paths,
                TypedNetworkKinds.LowVoltage,
                new float3(0f),
                150f,
                out UtilityConnectionTarget nearest);

            Assert.True(found);
            Assert.Equal(40f, nearest.Position.x, 3);
        }

        [Fact]
        public void Wrong_type_only_does_not_fall_back_to_nearest_path()
        {
            PlacementUtilityPath[] paths =
            {
                Path(TypedNetworkKinds.Water, 10f),
            };

            bool found = PlacementSearchMath.TryFindNearestUtilityPoint(
                paths,
                TypedNetworkKinds.LowVoltage,
                new float3(0f),
                150f,
                out _);

            Assert.False(found);
        }

        [Fact]
        public void Sewage_selects_matching_farther_path_over_water()
        {
            PlacementUtilityPath[] paths =
            {
                Path(TypedNetworkKinds.Water, 10f),
                Path(TypedNetworkKinds.Sewage, 35f),
            };

            bool found = PlacementSearchMath.TryFindNearestUtilityPoint(
                paths,
                TypedNetworkKinds.Sewage,
                new float3(0f),
                150f,
                out UtilityConnectionTarget nearest);

            Assert.True(found);
            Assert.Equal(35f, nearest.Position.x, 3);
        }

        [Fact]
        public void Matching_path_beyond_maximum_distance_is_rejected()
        {
            PlacementUtilityPath[] paths =
            {
                Path(TypedNetworkKinds.LowVoltage, 151f),
            };

            bool found = PlacementSearchMath.TryFindNearestUtilityPoint(
                paths,
                TypedNetworkKinds.LowVoltage,
                new float3(0f),
                150f,
                out _);

            Assert.False(found);
        }

        [Fact]
        public void Combined_lane_flags_satisfy_one_required_utility()
        {
            PlacementUtilityPath[] paths =
            {
                Path(TypedNetworkKinds.Water | TypedNetworkKinds.Sewage, 24f),
            };

            bool found = PlacementSearchMath.TryFindNearestUtilityPoint(
                paths,
                TypedNetworkKinds.Water,
                new float3(0f),
                150f,
                out _);

            Assert.True(found);
        }

        [Fact]
        public void Node_snap_ignores_nearer_interior_and_selects_endpoint()
        {
            Entity edge = new Entity { Index = 8, Version = 2 };
            PlacementUtilityPath[] paths =
            {
                new PlacementUtilityPath(
                    TypedNetworkKinds.LowVoltage,
                    edge,
                    new float2(0f, 1f),
                    new[]
                    {
                        new float3(0f, 0f, 0f),
                        new float3(0f, 0f, 50f),
                        new float3(0f, 0f, 100f),
                    },
                    nodeSnap: true),
            };

            bool found = PlacementSearchMath.TryFindNearestUtilityPoint(
                paths,
                TypedNetworkKinds.LowVoltage,
                new float3(10f, 0f, 50f),
                150f,
                out UtilityConnectionTarget nearest);

            Assert.True(found);
            Assert.Equal(edge, nearest.ParentEdge);
            Assert.Equal(0f, nearest.ParentSplit, 3);
            Assert.Equal(0f, nearest.Position.z, 3);
        }

        [Fact]
        public void Node_snap_rejects_when_only_interior_is_within_range()
        {
            PlacementUtilityPath[] paths =
            {
                new PlacementUtilityPath(
                    TypedNetworkKinds.Water,
                    new Entity { Index = 3, Version = 1 },
                    new float2(0f, 1f),
                    new[]
                    {
                        new float3(0f, 0f, 0f),
                        new float3(0f, 0f, 400f),
                    },
                    nodeSnap: true),
            };

            bool found = PlacementSearchMath.TryFindNearestUtilityPoint(
                paths,
                TypedNetworkKinds.Water,
                new float3(10f, 0f, 200f),
                150f,
                out _);

            Assert.False(found);
        }

        [Fact]
        public void Node_snap_maps_reversed_lane_end_to_parent_node()
        {
            Entity edge = new Entity { Index = 9, Version = 1 };
            PlacementUtilityPath[] paths =
            {
                new PlacementUtilityPath(
                    TypedNetworkKinds.Sewage,
                    edge,
                    new float2(1f, 0f),
                    new[]
                    {
                        new float3(0f, 0f, 100f),
                        new float3(0f, 0f, 0f),
                    },
                    nodeSnap: true),
            };

            bool found = PlacementSearchMath.TryFindNearestUtilityPoint(
                paths,
                TypedNetworkKinds.Sewage,
                new float3(5f, 0f, 90f),
                150f,
                out UtilityConnectionTarget nearest);

            Assert.True(found);
            Assert.Equal(1f, nearest.ParentSplit, 3);
        }

        [Fact]
        public void Nearest_point_carries_parent_edge_and_maps_reversed_lane_parameter()
        {
            Entity edge = new Entity { Index = 42, Version = 7 };
            PlacementUtilityPath[] paths =
            {
                new PlacementUtilityPath(
                    TypedNetworkKinds.LowVoltage,
                    edge,
                    new float2(1f, 0f),
                    new[]
                    {
                        new float3(0f, 0f, 0f),
                        new float3(0f, 0f, 100f),
                    }),
            };

            bool found = PlacementSearchMath.TryFindNearestUtilityPoint(
                paths,
                TypedNetworkKinds.LowVoltage,
                new float3(10f, 0f, 25f),
                150f,
                out UtilityConnectionTarget nearest);

            Assert.True(found);
            Assert.Equal(edge, nearest.ParentEdge);
            Assert.Equal(0.75f, nearest.ParentSplit, 3);
        }

        [Fact]
        public void Nearest_point_maps_partial_lane_parameter_to_parent_split()
        {
            Entity edge = new Entity { Index = 17, Version = 3 };
            PlacementUtilityPath[] paths =
            {
                new PlacementUtilityPath(
                    TypedNetworkKinds.Water,
                    edge,
                    new float2(0.5f, 1f),
                    new[]
                    {
                        new float3(0f, 0f, 0f),
                        new float3(0f, 0f, 100f),
                    }),
            };

            bool found = PlacementSearchMath.TryFindNearestUtilityPoint(
                paths,
                TypedNetworkKinds.Water,
                new float3(10f, 0f, 50f),
                150f,
                out UtilityConnectionTarget nearest);

            Assert.True(found);
            Assert.Equal(edge, nearest.ParentEdge);
            Assert.Equal(0.75f, nearest.ParentSplit, 3);
        }

        [Theory]
        [InlineData(0f, true)]
        [InlineData(0.4f, false)]
        [InlineData(1f, true)]
        public void Parent_edge_endpoints_resolve_to_native_nodes(
            float split,
            bool expectsNode)
        {
            Entity edge = new Entity { Index = 10, Version = 1 };
            Entity start = new Entity { Index = 11, Version = 1 };
            Entity end = new Entity { Index = 12, Version = 1 };

            PlacementSearchMath.ResolveConnectionAnchor(
                edge,
                start,
                end,
                split,
                out Entity anchor,
                out float anchorSplit);

            Assert.Equal(expectsNode ? (split <= 0f ? start : end) : edge, anchor);
            Assert.Equal(split, anchorSplit, 3);
        }

        private static PlacementUtilityPath Path(TypedNetworkKinds kinds, float x)
        {
            return new PlacementUtilityPath(
                kinds,
                new Entity { Index = (int)x + 1, Version = 1 },
                new float2(0f, 1f),
                new[]
                {
                    new float3(x, 0f, -10f),
                    new float3(x, 0f, 10f),
                });
        }
    }
}
