using System.Collections.Generic;
using Xunit;

namespace CS2MCP
{
    public sealed class NetworkBuildArgumentsTests
    {
        [Fact]
        public void Road_defaults_to_ground_without_elevation()
        {
            bool parsed = Parse(true, Query(), out NetworkBuildArguments arguments, out _);

            Assert.True(parsed);
            Assert.Equal(RoadBuildMode.Ground, arguments.RoadMode);
            Assert.False(arguments.HasControlPoint);
            Assert.False(arguments.HasElevation);
        }

        [Fact]
        public void Road_accepts_explicit_ground_mode()
        {
            bool parsed = Parse(
                true,
                Query(("mode", "ground")),
                out NetworkBuildArguments arguments,
                out _);

            Assert.True(parsed);
            Assert.Equal(RoadBuildMode.Ground, arguments.RoadMode);
        }

        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("bridge")]
        public void Road_rejects_unknown_or_empty_mode(string mode)
        {
            bool parsed = Parse(true, Query(("mode", mode)), out _, out string error);

            Assert.False(parsed);
            Assert.Contains("ground", error);
            Assert.Contains("grade-separated", error);
        }

        [Fact]
        public void Utility_has_no_road_mode()
        {
            bool parsed = Parse(false, Query(), out NetworkBuildArguments arguments, out _);

            Assert.True(parsed);
            Assert.Null(arguments.RoadMode);
        }

        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("ground")]
        [InlineData("grade-separated")]
        public void Utility_rejects_any_explicit_mode(string mode)
        {
            bool parsed = Parse(false, Query(("mode", mode)), out _, out string error);

            Assert.False(parsed);
            Assert.Contains("only valid for road prefabs", error);
        }

        [Theory]
        [InlineData("cx", "10")]
        [InlineData("cz", "10")]
        public void Control_point_requires_both_coordinates(string key, string value)
        {
            bool parsed = Parse(true, Query((key, value)), out _, out string error);

            Assert.False(parsed);
            Assert.Contains("both cx and cz", error);
        }

        [Fact]
        public void Malformed_control_point_is_not_silently_treated_as_straight()
        {
            bool parsed = Parse(
                true,
                Query(("cx", "not-a-number"), ("cz", "also-not-a-number")),
                out _,
                out string error);

            Assert.False(parsed);
            Assert.Contains("finite world coordinates", error);
        }

        [Fact]
        public void Valid_control_point_is_preserved()
        {
            bool parsed = Parse(
                true,
                Query(("cx", "12.5"), ("cz", "-7.25")),
                out NetworkBuildArguments arguments,
                out _);

            Assert.True(parsed);
            Assert.True(arguments.HasControlPoint);
            Assert.Equal(12.5f, arguments.ControlX);
            Assert.Equal(-7.25f, arguments.ControlZ);
        }

        [Fact]
        public void Ground_road_rejects_elevation()
        {
            bool parsed = Parse(true, Query(("e1", "5")), out _, out string error);

            Assert.False(parsed);
            Assert.Contains("does not accept e1/e2", error);
        }

        [Theory]
        [InlineData("e1")]
        [InlineData("e2")]
        public void Grade_separated_road_requires_both_elevations(string provided)
        {
            bool parsed = Parse(
                true,
                Query(("mode", "grade-separated"), (provided, "8")),
                out _,
                out string error);

            Assert.False(parsed);
            Assert.Contains("requires both e1 and e2", error);
        }

        [Fact]
        public void Grade_separated_road_requires_a_nonzero_elevation()
        {
            bool parsed = Parse(
                true,
                Query(("mode", "grade-separated"), ("e1", "0"), ("e2", "0")),
                out _,
                out string error);

            Assert.False(parsed);
            Assert.Contains("nonzero elevation", error);
        }

        [Fact]
        public void Grade_separated_road_preserves_both_elevations()
        {
            bool parsed = Parse(
                true,
                Query(("mode", "grade-separated"), ("e1", "8"), ("e2", "12")),
                out NetworkBuildArguments arguments,
                out _);

            Assert.True(parsed);
            Assert.Equal(RoadBuildMode.GradeSeparated, arguments.RoadMode);
            Assert.True(arguments.HasElevation);
            Assert.Equal(8f, arguments.StartElevation);
            Assert.Equal(12f, arguments.EndElevation);
        }

        [Fact]
        public void Utility_preserves_one_explicit_elevation_for_legacy_behavior()
        {
            bool parsed = Parse(
                false,
                Query(("e1", "-15")),
                out NetworkBuildArguments arguments,
                out _);

            Assert.True(parsed);
            Assert.True(arguments.HasElevation);
            Assert.Equal(-15f, arguments.StartElevation);
            Assert.Equal(0f, arguments.EndElevation);
        }

        [Theory]
        [InlineData("NaN")]
        [InlineData("Infinity")]
        [InlineData("61")]
        [InlineData("-31")]
        public void Elevation_must_be_finite_and_in_range(string elevation)
        {
            bool parsed = Parse(
                false,
                Query(("e1", elevation)),
                out _,
                out _);

            Assert.False(parsed);
        }

        private static bool Parse(
            bool isRoad,
            Dictionary<string, string> query,
            out NetworkBuildArguments arguments,
            out string error)
        {
            return NetworkBuildArguments.TryParse(
                query,
                isRoad,
                out arguments,
                out error);
        }

        private static Dictionary<string, string> Query(
            params (string Key, string Value)[] values)
        {
            var query = new Dictionary<string, string>();
            foreach ((string key, string value) in values)
            {
                query[key] = value;
            }
            return query;
        }
    }
}
