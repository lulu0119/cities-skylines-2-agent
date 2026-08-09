using System;
using System.Collections.Generic;
using Colossal;
using Colossal.IO.AssetDatabase;
using Game.Modding;
using Game.Settings;

namespace CitiesSkylines2Agent
{
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

        public Setting(IMod mod) : base(mod) { }

        // ---- Provider -------------------------------------

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

        // ---- Agent ---------------------------------------

        [SettingsUISection(kSection, kAgentGroup)]
        public bool AutoStart { get; set; } = true;

        [SettingsUISection(kSection, kAgentGroup)]
        public bool Continuous { get; set; } = true;

        // ---- Hidden --------------------------------------

        public string StartupPrompt { get; set; } = "";
        public int WindowTokens { get; set; } = 200_000;
        public float CompactThreshold { get; set; } = 0.85f;
        public int KeepTailMessages { get; set; } = 20;
        public int MaxToolRounds { get; set; } = 30;
        public bool EnableVisionTools { get; set; } = true;
        public int MaxSimWaitSeconds { get; set; } = 180;
        public string EnabledSkills { get; set; } = "utility-networks";

        // ---- Static facade ---------------------------------

        public static ProviderKind StaticProvider => Instance?.Provider ?? ProviderKind.OpenAI;
        public static string StaticEndpoint => Instance?.Endpoint ?? "https://api.openai.com/v1";
        public static string StaticModel => Instance?.Model ?? "";

        private const string DefaultStartupPrompt = "Observe the city and take the most impactful action to improve it.";

        public static string StaticStartupPrompt => string.IsNullOrWhiteSpace(Instance?.StartupPrompt)
            ? DefaultStartupPrompt : Instance.StartupPrompt;

        public static bool StaticAutoStart => Instance?.AutoStart ?? true;
        public static bool StaticContinuous => Instance?.Continuous ?? true;
        public static string StaticApiKey => Instance?.ApiKey ?? "";
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
            Continuous = true;
            StartupPrompt = "";
            WindowTokens = 200_000;
            CompactThreshold = 0.85f;
            KeepTailMessages = 20;
            MaxToolRounds = 30;
            EnableVisionTools = true;
            MaxSimWaitSeconds = 180;
            EnabledSkills = "utility-networks";
        }

        public void SetProviderWithEndpoint(ProviderKind kind)
        {
            Provider = kind;
            Endpoint = kind switch
            {
                ProviderKind.OpenAI => "https://api.openai.com/v1",
                ProviderKind.DeepSeek => "https://api.deepseek.com/v1",
                ProviderKind.OpenRouter => "https://openrouter.ai/api/v1",
                _ => Endpoint,
            };
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
                { m_Setting.GetOptionGroupLocaleID(Setting.kProviderGroup), "Provider" },
                { m_Setting.GetOptionGroupLocaleID(Setting.kAgentGroup), "Agent" },

                { m_Setting.GetOptionLabelLocaleID(nameof(Setting.Provider)), "Provider" },
                { m_Setting.GetOptionDescLocaleID(nameof(Setting.Provider)), "Select provider. Endpoint auto-filled." },
                { m_Setting.GetOptionLabelLocaleID(nameof(Setting.Endpoint)), "Endpoint" },
                { m_Setting.GetOptionDescLocaleID(nameof(Setting.Endpoint)), "API base URL. Auto-filled." },
                { m_Setting.GetOptionLabelLocaleID(nameof(Setting.ApiKey)), "API key" },
                { m_Setting.GetOptionDescLocaleID(nameof(Setting.ApiKey)), "Stored in settings only." },
                { m_Setting.GetOptionLabelLocaleID(nameof(Setting.Model)), "Model" },
                { m_Setting.GetOptionDescLocaleID(nameof(Setting.Model)), "e.g. gpt-5.6-sol, deepseek-v4-flash." },

                // Enum value locale keys: format is {SettingsLocaleID}.{PropertyName}.{EnumType}[{Value}]
                { m_Setting.GetSettingsLocaleID() + "." + nameof(Setting.Provider) + ".PROVIDERKIND[" + nameof(ProviderKind.OpenAI) + "]", "OpenAI" },
                { m_Setting.GetSettingsLocaleID() + "." + nameof(Setting.Provider) + ".PROVIDERKIND[" + nameof(ProviderKind.DeepSeek) + "]", "DeepSeek" },
                { m_Setting.GetSettingsLocaleID() + "." + nameof(Setting.Provider) + ".PROVIDERKIND[" + nameof(ProviderKind.OpenRouter) + "]", "OpenRouter" },
                { m_Setting.GetSettingsLocaleID() + "." + nameof(Setting.Provider) + ".PROVIDERKIND[" + nameof(ProviderKind.Custom) + "]", "Custom" },

                { m_Setting.GetOptionLabelLocaleID(nameof(Setting.AutoStart)), "Auto-start" },
                { m_Setting.GetOptionDescLocaleID(nameof(Setting.AutoStart)), "Start a turn on city load." },
                { m_Setting.GetOptionLabelLocaleID(nameof(Setting.Continuous)), "Continue" },
                { m_Setting.GetOptionDescLocaleID(nameof(Setting.Continuous)), "Keep the agent running without stopping." },
            };
        }

        public void Unload() { }
    }
}