namespace CS2MCP
{
    /// <summary>
    /// Decides which existing buildings the placement planner must treat as
    /// hard obstacles. Native-clearable growables stay out of this preflight;
    /// the game's validation and apply systems remain authoritative.
    /// </summary>
    internal static class PlacementObstaclePolicy
    {
        public static bool IsHardBuildingObstacle(
            bool spawnable,
            bool signature,
            bool overridable,
            bool deleteOverridden,
            bool attached,
            bool onFire,
            bool overridden)
        {
            if (overridden)
            {
                return false;
            }
            bool nativeClearable = spawnable
                && !signature
                && overridable
                && deleteOverridden;
            return !nativeClearable || attached || onFire;
        }
    }
}
