using System.Collections.Generic;
using Colossal;
using Colossal.IO.AssetDatabase;
using Game.Modding;
using Game.Settings;

namespace CitiesSkylines2Agent
{
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
        [SettingsUITextInput]
        public string Provider { get; set; } = "OpenAI-compatible";

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
        [SettingsUISlider(min = 16000f, max = 1000000f, step = 1000f)]
        public int WindowTokens { get; set; } = 200000;

        [SettingsUISection(kSection, kAgentGroup)]
        [SettingsUISlider(min = 0.50f, max = 0.99f, step = 0.01f)]
        public float CompactThreshold { get; set; } = 0.85f;

        [SettingsUISection(kSection, kAgentGroup)]
        [SettingsUISlider(min = 4f, max = 100f, step = 1f)]
        public int KeepTailMessages { get; set; } = 20;

        [SettingsUISection(kSection, kAgentGroup)]
        [SettingsUISlider(min = 1f, max = 200f, step = 1f)]
        public int MaxToolRounds { get; set; } = 30;

        // ---- Static facade for the agent loop ----------------------------

        public static string StaticProvider => Instance?.Provider ?? "OpenAI-compatible";
        public static string StaticEndpoint => Instance?.Endpoint ?? "https://api.openai.com/v1";
        public static string StaticModel => Instance?.Model ?? "";

        public static string StaticApiKey
        {
            get
            {
                return Instance?.ApiKey ?? "";
            }
        }

        public static long StaticWindowTokens => Instance?.WindowTokens ?? 200000;
        public static double StaticCompactThreshold => Instance?.CompactThreshold ?? 0.85;
        public static int StaticKeepTailMessages => Instance?.KeepTailMessages ?? 20;
        public static int StaticMaxToolRounds => Instance?.MaxToolRounds ?? 30;

        public override void SetDefaults()
        {
            Provider = "OpenAI-compatible";
            Endpoint = "https://api.openai.com/v1";
            ApiKey = "";
            Model = "";
            WindowTokens = 200000;
            CompactThreshold = 0.85f;
            KeepTailMessages = 20;
            MaxToolRounds = 30;
        }
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

                { m_Setting.GetOptionLabelLocaleID(nameof(Setting.Provider)), "Provider name" },
                { m_Setting.GetOptionDescLocaleID(nameof(Setting.Provider)), "Display name only (e.g. OpenAI, DeepSeek). The client is always built from Endpoint + API key." },
                { m_Setting.GetOptionLabelLocaleID(nameof(Setting.Endpoint)), "OpenAI-compatible endpoint" },
                { m_Setting.GetOptionDescLocaleID(nameof(Setting.Endpoint)), "Any OpenAI-compatible chat completions base URL, e.g. https://api.openai.com/v1 or https://api.deepseek.com/v1. Model must be filled in below." },
                { m_Setting.GetOptionLabelLocaleID(nameof(Setting.ApiKey)), "API key" },
                { m_Setting.GetOptionDescLocaleID(nameof(Setting.ApiKey)), "Stored only in this local settings file (never in the repo or logs). This is the only source of the key." },
                { m_Setting.GetOptionLabelLocaleID(nameof(Setting.Model)), "Model" },
                { m_Setting.GetOptionDescLocaleID(nameof(Setting.Model)), "Model id, e.g. gpt-5.5, deepseek-chat, claude-... (anything the endpoint accepts)." },

                { m_Setting.GetOptionLabelLocaleID(nameof(Setting.WindowTokens)), "Context window (tokens)" },
                { m_Setting.GetOptionDescLocaleID(nameof(Setting.WindowTokens)), "Upper bound used to decide when to compact. Set it to your provider's real limit." },
                { m_Setting.GetOptionLabelLocaleID(nameof(Setting.CompactThreshold)), "Compact threshold" },
                { m_Setting.GetOptionDescLocaleID(nameof(Setting.CompactThreshold)), "When estimated usage crosses this fraction of the window, older messages are summarized into a compact context block. Kept at 0.85 by default so normal turns stay prefix-cache friendly." },
                { m_Setting.GetOptionLabelLocaleID(nameof(Setting.KeepTailMessages)), "Keep last messages" },
                { m_Setting.GetOptionDescLocaleID(nameof(Setting.KeepTailMessages)), "Number of newest messages left verbatim when compaction runs." },
                { m_Setting.GetOptionLabelLocaleID(nameof(Setting.MaxToolRounds)), "Max tool rounds per turn" },
                { m_Setting.GetOptionDescLocaleID(nameof(Setting.MaxToolRounds)), "Safety cap on model+tool rounds in one user turn." },
            };
        }

        public void Unload()
        {
        }
    }
}
