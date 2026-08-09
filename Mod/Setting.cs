using System;
using System.Collections.Generic;
using Colossal;
using Colossal.IO.AssetDatabase;
using Game.Modding;
using Game.Settings;

namespace CitiesSkylines2Agent
{
    /// <summary>Pre-configured provider options. Picking one auto-fills the endpoint.</summary>
    public enum ProviderKind
    {
        OpenAI,
        DeepSeek,
        OpenRouter,
        Custom,
    }

    [FileLocation(nameof(CitiesSkylines2Agent))]
    [SettingsUIGroupOrder(kProviderGroup, kAgentGroup)]
    [SettingsUIShowGroupName(kProviderGroup, kAgentGroup)]
    public class Setting : ModSetting
    {
        public const string kSection = "Main";
        public const string kProviderGroup = "Provider";
        public const string kAgentGroup = "Agent";

        public static Setting Instance { get; set; }

        public Setting(IMod mod) : base(mod)
        {
        }

        // ---- Provider configuration -------------------------------------

        [SettingsUISection(kSection, kProviderGroup)]
        public ProviderKind Provider { get; set; } = ProviderKind.OpenAI;

        [SettingsUISection(kSection, kProviderGroup)]
        [SettingsUITextInput]
        public string Endpoint { get; set; } = "https://api.openai.com/v1";

        [SettingsUISection(kSection, kProviderGroup)]
        [SettingsUITextInput]
        public string ApiKey { get; set; } = "";

        [SettingsUISection(kSection, kProviderGroup)]
        [SettingsUITextInput]
        public string Model { get; set; } = "";

        // ---- Agent loop configuration -----------------------------------

        [SettingsUISection(kSection, kAgentGroup)]
        public bool AutoStart { get; set; } = true;

        [SettingsUISection(kSection, kAgentGroup)]
        [SettingsUITextInput]
        public string StartupPrompt { get; set; } =
            "Observe the current city, identify the highest-priority problem, and report one next step. Do not modify the city.";

        // ---- Hidden technical settings (not shown in UI) -----------------

        public int WindowTokens { get; set; } = 200_000;
        public float CompactThreshold { get; set; } = 0.85f;
        public int KeepTailMessages { get; set; } = 20;
        public int MaxToolRounds { get; set; } = 30;
        public bool EnableVisionTools { get; set; } = true;
        public int MaxSimWaitSeconds { get; set; } = 180;
        public string EnabledSkills { get; set; } = "utility-networks";

        // ---- Static facade for the agent loop ----------------------------

        public static ProviderKind StaticProvider => Instance?.Provider ?? ProviderKind.OpenAI;
        public static string StaticEndpoint => Instance?.Endpoint ?? "https://api.openai.com/v1";
        public static string StaticModel => Instance?.Model ?? "";
        public static bool StaticAutoStart => Instance?.AutoStart ?? true;
        public static string StaticStartupPrompt => Instance?.StartupPrompt ??
            "Observe the current city, identify the highest-priority problem, and report one next step. Do not modify the city.";

        public static string StaticApiKey
        {
            get
            {
                return Instance?.ApiKey ?? "";
            }
        }

        public static long StaticWindowTokens => Instance?.WindowTokens ?? 200_000;
        public static double StaticCompactThreshold => Instance?.CompactThreshold ?? 0.85;
        public static int StaticKeepTailMessages => Instance?.KeepTailMessages ?? 20;
        public static int StaticMaxToolRounds => Instance?.MaxToolRounds ?? 30;
        public static bool StaticEnableVisionTools => Instance?.EnableVisionTools ?? true;
        public static int StaticMaxSimWaitSeconds => Instance?.MaxSimWaitSeconds ?? 180;
        public static string StaticEnabledSkills => Instance?.EnabledSkills ?? "utility-networks";

        public override void SetDefaults()
        {
            Provider = ProviderKind.OpenAI;
            Endpoint = "https://api.openai.com/v1";
            ApiKey = "";
            Model = "";
            AutoStart = true;
            StartupPrompt = "Observe the current city, identify the highest-priority problem, and report one next step. Do not modify the city.";
            WindowTokens = 200_000;
            CompactThreshold = 0.85f;
            KeepTailMessages = 20;
            MaxToolRounds = 30;
            EnableVisionTools = true;
            MaxSimWaitSeconds = 180;
            EnabledSkills = "utility-networks";
        }

        // ---- Auto-fill endpoint when provider changes --------------------

        public void SetProviderWithEndpoint(ProviderKind kind)
        {
            Provider = kind;
            Endpoint = kind switch
            {
                ProviderKind.OpenAI => "https://api.openai.com/v1",
                ProviderKind.DeepSeek => "https://api.deepseek.com/v1",
                ProviderKind.OpenRouter => "https://openrouter.ai/api/v1",
                ProviderKind.Custom => Endpoint,
                _ => Endpoint,
            };
        }
        // Note: the game settings system reads/writes properties directly;
        // this helper is for programmatic use or mod config migration.
    }

    public class LocaleEN : IDictionarySource
    {
        private readonly Setting m_Setting;

        public LocaleEN(Setting setting)
        {
            m_Setting = setting;
        }

        public IEnumerable<KeyValuePair<string, string>> ReadEntries(IList<IDictionaryEntryError> errors, Dictionary<string, int> indexCounts)
        {
            return new Dictionary<string, string>
            {
                { m_Setting.GetSettingsLocaleID(), "Cities Skylines 2 Agent" },
                { m_Setting.GetOptionTabLocaleID(Setting.kSection), "Main" },
                { m_Setting.GetOptionGroupLocaleID(Setting.kProviderGroup), "Model provider" },
                { m_Setting.GetOptionGroupLocaleID(Setting.kAgentGroup), "Agent loop" },

                { m_Setting.GetOptionLabelLocaleID(nameof(Setting.Provider)), "Provider" },
                { m_Setting.GetOptionDescLocaleID(nameof(Setting.Provider)), "Select your model provider. Endpoint is auto-filled. Use Custom to enter a different endpoint." },
                { m_Setting.GetOptionLabelLocaleID(nameof(Setting.Endpoint)), "Endpoint" },
                { m_Setting.GetOptionDescLocaleID(nameof(Setting.Endpoint)), "OpenAI-compatible API base URL. Auto-filled by provider selection." },
                { m_Setting.GetOptionLabelLocaleID(nameof(Setting.ApiKey)), "API key" },
                { m_Setting.GetOptionDescLocaleID(nameof(Setting.ApiKey)), "Stored only in this local settings file (never in the repo or logs)." },
                { m_Setting.GetOptionLabelLocaleID(nameof(Setting.Model)), "Model" },
                { m_Setting.GetOptionDescLocaleID(nameof(Setting.Model)), "Model id, e.g. gpt-5.6-sol, deepseek-v4-flash, or any model your endpoint accepts." },

                { m_Setting.GetOptionLabelLocaleID(nameof(Setting.AutoStart)), "Auto-start on city load" },
                { m_Setting.GetOptionDescLocaleID(nameof(Setting.AutoStart)), "Start one agent turn automatically after a city finishes loading; leaving the city arms it again." },
                { m_Setting.GetOptionLabelLocaleID(nameof(Setting.StartupPrompt)), "Startup prompt" },
                { m_Setting.GetOptionDescLocaleID(nameof(Setting.StartupPrompt)), "User message queued automatically on city load. Keep it empty to disable the automatic turn." },
            };
        }

        public void Unload()
        {
        }
    }
}