using System;
using System.Collections.Generic;
using Unity.Mathematics;
using Xunit;

namespace CS2MCP
{
    public sealed class OperationalAreaPlanningMathTests
    {
        [Fact]
        public void Expansion_preserves_locked_edge_and_exceeds_the_old_fan_width()
        {
            List<float2> existing = Rectangle();
            bool planned = Plan(existing, 2500f, 0f, Array.Empty<OperationalAreaObstacle>(), out List<float2> polygon, out float area, out string error);
            Assert.True(planned, error);
            Assert.True(area >= 2500f);
            AssertLockedEdge(polygon);
            Assert.InRange(polygon.Count, 4, 16);
            Assert.True(OperationalAreaPlanningMath.HasMinimumSpacing(polygon, 4f));
            Assert.True(MaxAngleFromNormal(polygon) > math.radians(70f));
        }

        [Fact]
        public void Expansion_is_expansion_only()
        {
            List<float2> existing = Rectangle();
            float existingArea = OperationalAreaPlanningMath.PolygonArea(existing);

            Assert.True(Plan(existing, existingArea + 400f, 0f, Array.Empty<OperationalAreaObstacle>(), out List<float2> polygon, out float area, out string error), error);
            Assert.True(area >= existingArea);
            foreach (float2 vertex in existing)
            {
                Assert.True(OperationalAreaPlanningMath.DistanceToPolygon(vertex, polygon) <= 0.5f);
            }
        }

        [Fact]
        public void Obstacle_in_front_is_clipped_instead_of_rejecting_the_plan()
        {
            List<float2> existing = Rectangle();
            var obstacle = new OperationalAreaObstacle(new float2(0f, 55f), 10f);

            Assert.True(Plan(existing, 2200f, 0f, new[] { obstacle }, out List<float2> polygon, out float area, out string error), error);
            Assert.True(area >= 2200f);
            AssertLockedEdge(polygon);
            Assert.True(
                OperationalAreaPlanningMath.DistanceToPolygon(obstacle.Center, polygon) >= obstacle.Radius);
            Assert.True(MaxAngleFromNormal(polygon) > math.radians(70f));
        }

        [Fact]
        public void Obstacle_that_caps_growth_below_target_fails()
        {
            List<float2> existing = Rectangle();
            var wall = new[]
            {
                new OperationalAreaObstacle(new float2(0f, 28f), 18f),
                new OperationalAreaObstacle(new float2(-22f, 20f), 14f),
                new OperationalAreaObstacle(new float2(22f, 20f), 14f),
            };

            Assert.False(Plan(existing, 20000f, 0f, wall, out _, out _, out _));
        }

        [Fact]
        public void Center_shifts_include_a_zero_offset()
        {
            float[] shifts = OperationalAreaPlanningMath.CenterShifts(40f);
            Assert.Equal(5, shifts.Length);
            Assert.Equal(0f, shifts[0]);
            Assert.Contains(-20f, shifts);
            Assert.Contains(20f, shifts);
        }

        [Fact]
        public void Shifted_center_still_preserves_the_locked_edge()
        {
            List<float2> existing = Rectangle();
            Assert.True(Plan(existing, 2000f, 10f, Array.Empty<OperationalAreaObstacle>(), out List<float2> polygon, out _, out string error), error);
            AssertLockedEdge(polygon);
            Assert.InRange(polygon.Count, 4, 16);
        }

        private static bool Plan(
            List<float2> existing,
            float targetArea,
            float tangentShift,
            IReadOnlyList<OperationalAreaObstacle> obstacles,
            out List<float2> polygon,
            out float area,
            out string error)
        {
            return OperationalAreaPlanningMath.TryPlanExpansion(
                existing,
                existing[0],
                existing[1],
                new float2(1f, 0f),
                new float2(0f, 1f),
                targetArea,
                tangentShift,
                obstacles,
                out polygon,
                out area,
                out error);
        }

        private static List<float2> Rectangle()
        {
            return new List<float2>
            {
                new float2(-20f, 0f),
                new float2(20f, 0f),
                new float2(20f, 20f),
                new float2(-20f, 20f),
            };
        }

        private static void AssertLockedEdge(List<float2> polygon)
        {
            Assert.True(math.distancesq(polygon[0], new float2(-20f, 0f)) < 0.01f);
            Assert.True(math.distancesq(polygon[1], new float2(20f, 0f)) < 0.01f);
        }

        private static float MaxAngleFromNormal(List<float2> polygon)
        {
            float2 lockedMid = new float2(0f, 0f);
            float2 normal = new float2(0f, 1f);
            float2 tangent = new float2(1f, 0f);
            float widest = 0f;
            for (int i = 2; i < polygon.Count; i++)
            {
                float2 delta = polygon[i] - lockedMid;
                float angle = math.abs(math.atan2(math.dot(delta, tangent), math.dot(delta, normal)));
                widest = math.max(widest, angle);
            }
            return widest;
        }
    }
}
