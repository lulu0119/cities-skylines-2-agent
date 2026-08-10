using System;
using System.IO;

namespace CitiesSkylines2Agent.Agent
{
    /// <summary>Filesystem locations for agent state and logs.</summary>
    /// <remarks>Runtime files stay outside the watched mod asset directory.</remarks>
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
                // LocalLow is a sibling of Local, not Local\Low.
                string localLow = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                    "AppData",
                    "LocalLow",
                    "Colossal Order",
                    "Cities Skylines II");
                return localLow;
            }
        }

        public static string ModDataDirectory => Path.Combine(UserDataRoot, "Mods", ModId);

        public static string RuntimeDataDirectory => Path.Combine(UserDataRoot, ModId);

        public static string LogsDirectory => Path.Combine(RuntimeDataDirectory, "logs");

        public static string ScreenshotsDirectory => Path.Combine(LogsDirectory, "screenshots");

        public static string StateDirectory => Path.Combine(RuntimeDataDirectory, "state");

        public static string ContextBlocksFile => Path.Combine(StateDirectory, "context-blocks.json");

        /// <summary>
        /// Development payloads live outside Mods so rebuilding them does not
        /// trigger the game's asset watcher or a Gameface media reload.
        /// </summary>
        public static string HotReloadDirectory => Path.Combine(RuntimeDataDirectory, "hot-reload");

        public static string HotReloadHandlersFile =>
            Path.Combine(HotReloadDirectory, "RequestHandlers.dll");

        public static string HotReloadToolCatalogFile =>
            Path.Combine(HotReloadDirectory, "ToolCatalog.json");

        public static string HotReloadSkillsDirectory =>
            Path.Combine(HotReloadDirectory, "Skills");

        public static void EnsureDirectories()
        {
            Directory.CreateDirectory(ModDataDirectory);
            Directory.CreateDirectory(RuntimeDataDirectory);
            Directory.CreateDirectory(LogsDirectory);
            Directory.CreateDirectory(ScreenshotsDirectory);
            Directory.CreateDirectory(StateDirectory);
            Directory.CreateDirectory(HotReloadDirectory);
        }
    }
}
