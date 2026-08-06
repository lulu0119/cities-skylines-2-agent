using System;
using System.IO;

namespace CitiesSkylines2Agent.Agent
{
    /// <summary>Filesystem locations for agent state and logs.</summary>
    public static class ModPaths
    {
        public const string ModId = "CitiesSkylines2Agent";

        /// <summary>
        /// User data root: CSII_USERDATAPATH when set (dev builds), otherwise
        /// the game's LocalLow profile. Never contains API keys.
        /// </summary>
        public static string UserDataRoot
        {
            get
            {
                string env = Environment.GetEnvironmentVariable("CSII_USERDATAPATH");
                if (!string.IsNullOrEmpty(env))
                {
                    return env;
                }
                string localLow = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "Low",
                    "Colossal Order",
                    "Cities Skylines II");
                return localLow;
            }
        }

        public static string ModDataDirectory => Path.Combine(UserDataRoot, "Mods", ModId);

        public static string LogsDirectory => Path.Combine(ModDataDirectory, "logs");

        public static string ScreenshotsDirectory => Path.Combine(LogsDirectory, "screenshots");

        public static string StateDirectory => Path.Combine(ModDataDirectory, "state");

        public static string PlanFile => Path.Combine(StateDirectory, "plan.json");

        public static string ContextBlocksFile => Path.Combine(StateDirectory, "context-blocks.json");

        public static void EnsureDirectories()
        {
            Directory.CreateDirectory(ModDataDirectory);
            Directory.CreateDirectory(LogsDirectory);
            Directory.CreateDirectory(ScreenshotsDirectory);
            Directory.CreateDirectory(StateDirectory);
        }
    }
}
