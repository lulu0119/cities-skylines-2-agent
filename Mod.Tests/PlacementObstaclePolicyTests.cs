using Xunit;

namespace CS2MCP
{
    public sealed class PlacementObstaclePolicyTests
    {
        [Fact]
        public void Ordinary_growable_matches_native_clearance_shape()
        {
            Assert.False(PlacementObstaclePolicy.IsHardBuildingObstacle(
                spawnable: true,
                signature: false,
                overridable: true,
                deleteOverridden: true,
                attached: false,
                onFire: false,
                overridden: false));
        }

        [Theory]
        [InlineData(false, false, true, true)]
        [InlineData(true, true, true, true)]
        [InlineData(true, false, false, true)]
        [InlineData(true, false, true, false)]
        public void Unknown_signature_or_non_overridable_building_is_not_native_clearable(
            bool spawnable,
            bool signature,
            bool overridable,
            bool deleteOverridden)
        {
            Assert.True(PlacementObstaclePolicy.IsHardBuildingObstacle(
                spawnable,
                signature,
                overridable,
                deleteOverridden,
                attached: false,
                onFire: false,
                overridden: false));
        }

        [Theory]
        [InlineData(true, false)]
        [InlineData(false, true)]
        public void Attached_or_burning_growable_is_a_hard_obstacle(
            bool attached,
            bool onFire)
        {
            Assert.True(PlacementObstaclePolicy.IsHardBuildingObstacle(
                spawnable: true,
                signature: false,
                overridable: true,
                deleteOverridden: true,
                attached,
                onFire,
                overridden: false));
        }

        [Fact]
        public void Already_overridden_building_is_not_reintroduced_as_an_obstacle()
        {
            Assert.False(PlacementObstaclePolicy.IsHardBuildingObstacle(
                spawnable: false,
                signature: true,
                overridable: false,
                deleteOverridden: false,
                attached: true,
                onFire: true,
                overridden: true));
        }
    }
}
