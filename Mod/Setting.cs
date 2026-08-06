using System.Collections.Generic;
using Colossal;
using Colossal.IO.AssetDatabase;
using Game.Modding;
using Game.Settings;

namespace CitiesSkylines2Agent
{
    [FileLocation(nameof(CitiesSkylines2Agent))]
    [SettingsUIGroupOrder(kMainGroup)]
    [SettingsUIShowGroupName(kMainGroup)]
    public class Setting : ModSetting
    {
        public const string kSection = "Main";
        public const string kMainGroup = "Agent";

        public Setting(IMod mod) : base(mod)
        {
        }

        [SettingsUISection(kSection, kMainGroup)]
        public bool ChatPanelHint { get; set; } = true;

        public override void SetDefaults()
        {
            ChatPanelHint = true;
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
                { m_Setting.GetOptionGroupLocaleID(Setting.kMainGroup), "Chat" },
                { m_Setting.GetOptionLabelLocaleID(nameof(Setting.ChatPanelHint)), "In-game chat panel (GameBottomRight + Portal)" },
                { m_Setting.GetOptionDescLocaleID(nameof(Setting.ChatPanelHint)), "Local echo shell until C# IChatClient agent is wired. Tools will run on ToolQueueSystem (UIUpdate)." },
            };
        }

        public void Unload()
        {
        }
    }
}
