using System;
using System.Collections.Generic;
using Colossal;
using Colossal.IO.AssetDatabase;
using Game.Modding;
using Game.Settings;

namespace CitiesSkylines2Agent
{
    public enum VisionToolMode
    {
        Auto,
        On,
        Off,
    }

    [FileLocation(nameof(CitiesSkylines2Agent))]
    [SettingsUIGroupOrder(kConnectionGroup, kAgentGroup)]
    [SettingsUIShowGroupName(kConnectionGroup, kAgentGroup)]
    public class Setting : ModSetting
    {
        public const string kSection = "Main";
        public const string kConnectionGroup = "Connection";
        public const string kAgentGroup = "Agent";

        public static Setting Instance { get; set; }

        public Setting(IMod mod) : base(mod) { }

        // ---- Connection -----------------------------------

        [SettingsUISection(kSection, kConnectionGroup)]
        [SettingsUITextInput]
        public string Endpoint { get; set; } = "https://api.openai.com/v1";

        [SettingsUISection(kSection, kConnectionGroup)]
        [SettingsUITextInput]
        public string ApiKey { get; set; } = "";

        [SettingsUISection(kSection, kConnectionGroup)]
        [SettingsUITextInput]
        public string Model { get; set; } = "";

        // ---- Agent ---------------------------------------

        [SettingsUISection(kSection, kAgentGroup)]
        public bool AutoStart { get; set; } = true;

        [SettingsUISection(kSection, kAgentGroup)]
        public bool Continuous { get; set; } = true;

        [SettingsUISection(kSection, kAgentGroup)]
        public bool AllowProgressionPurchases { get; set; } = true;

        [SettingsUISection(kSection, kAgentGroup)]
        public bool AllowDemolition { get; set; } = true;

        [SettingsUISection(kSection, kAgentGroup)]
        public bool EnableDevelopmentTools { get; set; } = false;

        [SettingsUISection(kSection, kAgentGroup)]
        public VisionToolMode VisionTools { get; set; } = VisionToolMode.Auto;

        // ---- Hidden --------------------------------------

        public string StartupPrompt { get; set; } = "";
        public int WindowTokens { get; set; } = 200_000;

        // ---- Static facade ---------------------------------

        public static string StaticEndpoint => Instance?.Endpoint ?? "https://api.openai.com/v1";
        public static string StaticModel => Instance?.Model ?? "";

        private const string DefaultStartupPrompt = "持续经营城市：先解决当前限制发展的问题，再按需求扩张；不要只报告，要行动。";

        public static string StaticStartupPrompt => string.IsNullOrWhiteSpace(Instance?.StartupPrompt)
            ? DefaultStartupPrompt : Instance.StartupPrompt;

        public static bool StaticAutoStart => Instance?.AutoStart ?? true;
        public static bool StaticContinuous => Instance?.Continuous ?? true;
        public static bool StaticAllowProgressionPurchases =>
            Instance?.AllowProgressionPurchases ?? true;
        public static bool StaticAllowDemolition => Instance?.AllowDemolition ?? true;
        public static bool StaticEnableDevelopmentTools =>
            Instance?.EnableDevelopmentTools ?? false;
        public static VisionToolMode StaticVisionToolMode =>
            Instance?.VisionTools ?? VisionToolMode.Auto;
        public static string StaticApiKey => Instance?.ApiKey ?? "";
        public static long StaticWindowTokens => Instance?.WindowTokens ?? 200_000;

        public override void SetDefaults()
        {
            Endpoint = "https://api.openai.com/v1";
            ApiKey = "";
            Model = "";
            AutoStart = true;
            Continuous = true;
            AllowProgressionPurchases = true;
            AllowDemolition = true;
            EnableDevelopmentTools = false;
            VisionTools = VisionToolMode.Auto;
            StartupPrompt = "";
            WindowTokens = 200_000;
        }
    }

    public class LocaleEN : IDictionarySource
    {
        private readonly Setting m_Setting;

        public LocaleEN(Setting setting) { m_Setting = setting; }

        public IEnumerable<KeyValuePair<string, string>> ReadEntries(IList<IDictionaryEntryError> errors, Dictionary<string, int> indexCounts)
        {
            return new Dictionary<string, string>
            {
                { m_Setting.GetSettingsLocaleID(), "Cities Skylines 2 Agent" },
                { m_Setting.GetOptionTabLocaleID(Setting.kSection), "Main" },
                { m_Setting.GetOptionGroupLocaleID(Setting.kConnectionGroup), "Connection" },
                { m_Setting.GetOptionGroupLocaleID(Setting.kAgentGroup), "Agent" },

                { m_Setting.GetOptionLabelLocaleID(nameof(Setting.Endpoint)), "Endpoint" },
                { m_Setting.GetOptionDescLocaleID(nameof(Setting.Endpoint)), "OpenAI-compatible API base URL." },
                { m_Setting.GetOptionLabelLocaleID(nameof(Setting.ApiKey)), "API key" },
                { m_Setting.GetOptionDescLocaleID(nameof(Setting.ApiKey)), "Stored in settings only." },
                { m_Setting.GetOptionLabelLocaleID(nameof(Setting.Model)), "Model" },
                { m_Setting.GetOptionDescLocaleID(nameof(Setting.Model)), "e.g. gpt-5.6-sol, deepseek-v4-flash." },

                { m_Setting.GetOptionLabelLocaleID(nameof(Setting.AutoStart)), "Auto-start" },
                { m_Setting.GetOptionDescLocaleID(nameof(Setting.AutoStart)), "Start a turn on city load." },
                { m_Setting.GetOptionLabelLocaleID(nameof(Setting.Continuous)), "Continue" },
                { m_Setting.GetOptionDescLocaleID(nameof(Setting.Continuous)), "Keep the agent running without stopping." },
                { m_Setting.GetOptionLabelLocaleID(nameof(Setting.AllowProgressionPurchases)), "Allow development purchases" },
                { m_Setting.GetOptionDescLocaleID(nameof(Setting.AllowProgressionPurchases)), "Let the agent spend earned Development Points on the Development Tree." },
                { m_Setting.GetOptionLabelLocaleID(nameof(Setting.AllowDemolition)), "Allow demolition" },
                { m_Setting.GetOptionDescLocaleID(nameof(Setting.AllowDemolition)), "Let the agent bulldoze buildings and road segments without a confirmation dialog." },
                { m_Setting.GetOptionLabelLocaleID(nameof(Setting.EnableDevelopmentTools)), "Development / acceptance tools" },
                { m_Setting.GetOptionDescLocaleID(nameof(Setting.EnableDevelopmentTools)), "Expose diagnostic, experimental, and manual-save tools to the in-game agent." },
                { m_Setting.GetOptionLabelLocaleID(nameof(Setting.VisionTools)), "Visual tools" },
                { m_Setting.GetOptionDescLocaleID(nameof(Setting.VisionTools)), "Auto follows model-name capabilities; On and Off force the result." },

                { m_Setting.GetEnumValueLocaleID(VisionToolMode.Auto), "Auto" },
                { m_Setting.GetEnumValueLocaleID(VisionToolMode.On), "On" },
                { m_Setting.GetEnumValueLocaleID(VisionToolMode.Off), "Off" },
            };
        }

        public void Unload() { }
    }
}
