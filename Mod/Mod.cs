using Colossal.IO.AssetDatabase;
using Colossal.Logging;
using Game;
using Game.Modding;
using Game.SceneFlow;
using CitiesSkylines2Agent.Agent;

namespace CitiesSkylines2Agent
{
    public class Mod : IMod
    {
        public static ILog log = LogManager.GetLogger($"{nameof(CitiesSkylines2Agent)}.{nameof(Mod)}").SetShowsErrorsInUI(false);
        private Setting m_Setting;

        public void OnLoad(UpdateSystem updateSystem)
        {
            log.Info(nameof(OnLoad));

            if (GameManager.instance.modManager.TryGetExecutableAsset(this, out var asset))
            {
                log.Info($"Current mod asset at {asset.path}");
            }

            m_Setting = new Setting(this);
            m_Setting.RegisterInOptionsUI();
            GameManager.instance.localizationManager.AddSource("en-US", new LocaleEN(m_Setting));
            AssetDatabase.global.LoadSettings(nameof(CitiesSkylines2Agent), m_Setting, new Setting(this));
            Setting.Instance = m_Setting;

            AgentLoop.EnsureCreated();

            // UIUpdate keeps running while the simulation is paused.
            updateSystem.UpdateAt<ToolQueueSystem>(SystemUpdatePhase.UIUpdate);
            updateSystem.UpdateAt<CS2MCP.BridgeSystem>(SystemUpdatePhase.UIUpdate);
            updateSystem.UpdateAt<CS2MCP.BridgeToolSystem>(SystemUpdatePhase.ToolUpdate);
            updateSystem.UpdateAt<AgentUISystem>(SystemUpdatePhase.UIUpdate);
        }

        public void OnDispose()
        {
            log.Info(nameof(OnDispose));
            AgentLoop.Instance?.Dispose();
            Setting.Instance = null;
            if (m_Setting != null)
            {
                m_Setting.UnregisterInOptionsUI();
                m_Setting = null;
            }
        }
    }
}
