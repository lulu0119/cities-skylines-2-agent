using System;
using System.Collections.Generic;
using Unity.Mathematics;
using Xunit;

namespace CS2MCP
{
    public sealed class RoadGroundPreflightTests
    {
        [Fact]
        public void Dry_flat_ground_road_is_allowed()
        {
            var surface = new RecordingSurface(_ => Dry);

            RoadGroundPreflightResult result = Evaluate(
                Straight(new float3(0f, 0f, 0f), new float3(20f, 0f, 0f)),
                surface);

            Assert.True(result.Allowed);
            Assert.Equal(0, surface.Positions.Count % 3);
            for (int i = 4; i < surface.Positions.Count; i += 3)
            {
                Assert.InRange(
                    math.distance(
                        surface.Positions[i - 3].xz,
                        surface.Positions[i].xz),
                    0f,
                    4.001f);
            }
        }

        [Fact]
        public void Water_between_dry_endpoints_is_rejected()
        {
            var surface = new RecordingSurface(position =>
                position.x >= 8f && position.x <= 12f
                    ? new RoadSurfaceSample(1f, 0.5f)
                    : Dry);

            RoadGroundPreflightResult result = Evaluate(
                Straight(new float3(0f), new float3(20f, 0f, 0f)),
                surface);

            Assert.Equal(RoadGroundBlock.Water, result.Block);
            Assert.InRange(result.Position.x, 8f, 12f);
            Assert.Equal(0.5f, result.WaterDepth);
        }

        [Fact]
        public void Water_under_the_road_edge_is_rejected_when_centerline_is_dry()
        {
            var surface = new RecordingSurface(position =>
                math.abs(position.z) > 3.9f
                    ? new RoadSurfaceSample(1f, 0.5f)
                    : Dry);

            RoadGroundPreflightResult result = Evaluate(
                Straight(new float3(0f), new float3(20f, 0f, 0f)),
                surface);

            Assert.Equal(RoadGroundBlock.Water, result.Block);
            Assert.Equal(4f, math.abs(result.Position.z), 3);
        }

        [Fact]
        public void Water_between_centerline_and_edge_of_a_wide_road_is_rejected()
        {
            var surface = new RecordingSurface(position =>
                position.z > 3f && position.z < 5f
                    ? new RoadSurfaceSample(1f, 0.5f)
                    : Dry);

            RoadGroundPreflightResult result = RoadGroundPreflight.Evaluate(
                Straight(new float3(0f), new float3(20f, 0f, 0f)),
                8f,
                0.2f,
                0f,
                surface);

            Assert.Equal(RoadGroundBlock.Water, result.Block);
            Assert.Equal(4f, result.Position.z, 3);
        }

        [Fact]
        public void Water_on_curved_middle_is_rejected_even_when_chord_is_dry()
        {
            var path = new RoadPath(
                new float3(0f, 0f, 0f),
                new float3(0f, 0f, 20f),
                new float3(20f, 0f, 20f),
                new float3(20f, 0f, 0f));
            var surface = new RecordingSurface(position =>
                position.z > 10f
                    ? new RoadSurfaceSample(1f, 0.5f)
                    : Dry);

            RoadGroundPreflightResult result = Evaluate(path, surface);

            Assert.Equal(RoadGroundBlock.Water, result.Block);
            Assert.True(result.Position.z > 10f);
        }

        [Theory]
        [InlineData(0.199f, 1f)]
        [InlineData(0.2f, 0f)]
        public void Water_must_be_deep_enough_and_reach_the_road_surface(
            float waterDepth,
            float waterHeight)
        {
            var surface = new RecordingSurface(
                _ => new RoadSurfaceSample(waterHeight, waterDepth));

            RoadGroundPreflightResult result = Evaluate(
                Straight(new float3(0f), new float3(20f, 0f, 0f)),
                surface);

            Assert.True(result.Allowed);
        }

        [Fact]
        public void Minimum_surface_offset_is_applied_to_water_height()
        {
            var surface = new RecordingSurface(_ => new RoadSurfaceSample(0.4f, 1f));

            RoadGroundPreflightResult result = RoadGroundPreflight.Evaluate(
                Straight(new float3(0f), new float3(20f, 0f, 0f)),
                4f,
                0.2f,
                0.5f,
                surface);

            Assert.True(result.Allowed);
        }

        [Fact]
        public void Steep_middle_is_rejected_even_when_endpoints_have_equal_height()
        {
            var path = new RoadPath(
                new float3(0f, 0f, 0f),
                new float3(8f, 12f, 0f),
                new float3(12f, 12f, 0f),
                new float3(20f, 0f, 0f));

            RoadGroundPreflightResult result = Evaluate(
                path,
                new RecordingSurface(_ => Dry));

            Assert.Equal(RoadGroundBlock.SteepSlope, result.Block);
            Assert.True(result.Grade > 0.1f);
            Assert.InRange(result.Position.x, 0f, 20f);
        }

        [Theory]
        [InlineData(0.1f, true)]
        [InlineData(0.1001f, false)]
        public void Product_grade_at_threshold_is_allowed_but_higher_grade_is_rejected(
            float grade,
            bool allowed)
        {
            RoadPath path = Straight(
                new float3(0f, 0f, 0f),
                new float3(12f, grade * 12f, 0f));

            RoadGroundPreflightResult result = RoadGroundPreflight.Evaluate(
                path,
                4f,
                0.2f,
                0f,
                new RecordingSurface(_ => Dry));

            Assert.Equal(allowed, result.Allowed);
            Assert.Equal(0.1f, result.MaximumGrade, 3);
        }

        [Fact]
        public void Zero_prefab_slope_limit_still_uses_the_product_limit()
        {
            RoadGroundPreflightResult result = RoadGroundPreflight.Evaluate(
                Straight(new float3(0f, 0f, 0f), new float3(12f, 1.21f, 0f)),
                4f,
                0f,
                0f,
                new RecordingSurface(_ => Dry));

            Assert.Equal(RoadGroundBlock.SteepSlope, result.Block);
            Assert.Equal(0.1f, result.MaximumGrade, 3);
        }

        [Fact]
        public void Stricter_prefab_slope_limit_wins_over_product_limit()
        {
            RoadGroundPreflightResult result = RoadGroundPreflight.Evaluate(
                Straight(new float3(0f, 0f, 0f), new float3(20f, 1.7f, 0f)),
                4f,
                0.08f,
                0f,
                new RecordingSurface(_ => Dry));

            Assert.Equal(RoadGroundBlock.SteepSlope, result.Block);
            Assert.Equal(0.08f, result.MaximumGrade, 3);
        }

        [Fact]
        public void Gently_climbing_curve_is_allowed_by_discrete_grade_check()
        {
            var path = new RoadPath(
                new float3(0f, 0f, 0f),
                new float3(0f, 0f, 20f),
                new float3(20f, 2f, 20f),
                new float3(20f, 2f, 0f));

            RoadGroundPreflightResult result = RoadGroundPreflight.Evaluate(
                path,
                4f,
                0.15f,
                0f,
                new RecordingSurface(_ => Dry));

            Assert.True(result.Allowed);
        }

        [Fact]
        public void Distant_control_point_is_rejected_before_unbounded_sampling()
        {
            RoadGroundPreflightResult result = Evaluate(
                RoadPath.WithControlPoint(
                    new float3(0f),
                    new float3(10000f, 0f, 0f),
                    new float3(20f, 0f, 0f)),
                new RecordingSurface(_ => Dry));

            Assert.Equal(RoadGroundBlock.InvalidPath, result.Block);
        }

        [Fact]
        public void Reversing_a_path_preserves_the_water_conclusion()
        {
            RoadPath forward = Straight(new float3(0f), new float3(20f, 0f, 0f));
            var reverse = new RoadPath(forward.D, forward.C, forward.B, forward.A);
            Func<float3, RoadSurfaceSample> sample = position =>
                position.x >= 8f && position.x <= 12f
                    ? new RoadSurfaceSample(1f, 0.5f)
                    : Dry;

            RoadGroundPreflightResult first = Evaluate(
                forward,
                new RecordingSurface(sample));
            RoadGroundPreflightResult second = Evaluate(
                reverse,
                new RecordingSurface(sample));

            Assert.Equal(RoadGroundBlock.Water, first.Block);
            Assert.Equal(first.Block, second.Block);
        }

        [Fact]
        public void Degenerate_horizontal_tangent_does_not_produce_non_finite_samples()
        {
            var surface = new RecordingSurface(_ => Dry);
            var path = new RoadPath(
                new float3(5f, 0f, 5f),
                new float3(5f, 2f, 5f),
                new float3(5f, 4f, 5f),
                new float3(5f, 6f, 5f));

            RoadGroundPreflightResult result = Evaluate(path, surface);

            Assert.Equal(RoadGroundBlock.SteepSlope, result.Block);
            Assert.True(math.all(math.isfinite(result.Position)));
            Assert.All(surface.Positions, position =>
                Assert.True(math.all(math.isfinite(position))));
        }

        private static readonly RoadSurfaceSample Dry = new RoadSurfaceSample(0f, 0f);

        private static RoadGroundPreflightResult Evaluate(
            RoadPath path,
            IRoadSurfaceSampler surface)
        {
            return RoadGroundPreflight.Evaluate(
                path,
                4f,
                0.2f,
                0f,
                surface);
        }

        private static RoadPath Straight(float3 start, float3 end)
        {
            return RoadPath.Straight(start, end);
        }

        private sealed class RecordingSurface : IRoadSurfaceSampler
        {
            private readonly Func<float3, RoadSurfaceSample> m_Sample;

            public RecordingSurface(Func<float3, RoadSurfaceSample> sample)
            {
                m_Sample = sample;
            }

            public List<float3> Positions { get; } = new List<float3>();

            public RoadSurfaceSample Sample(float3 roadPosition)
            {
                Positions.Add(roadPosition);
                return m_Sample(roadPosition);
            }
        }
    }
}
