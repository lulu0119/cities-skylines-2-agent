using System.Collections.Concurrent;
using System.Text;
using System.Threading;
using Colossal.UI.Binding;
using Game;
using Game.SceneFlow;
using Game.UI;
using UnityEngine.Scripting;

namespace CitiesSkylines2Agent.Agent
{
    /// <summary>
    /// Bridges the agent loop to Gameface: publishes agent state and a live
    /// event stream (deltas, tool cards, status), and receives player commands
    /// (send / interrupt). Registered at UIUpdate so it keeps running while the
    /// simulation is paused.
    /// </summary>
    public sealed partial class AgentUISystem : UISystemBase
    {
        private const string Group = "CitiesSkylines2Agent";
        private const int MaxEventsPerUpdate = 32;

        private readonly ConcurrentQueue<AgentUiEvent> m_Events =
            new ConcurrentQueue<AgentUiEvent>();
        private ValueBinding<string> m_StateBinding;
        private EventBinding<string> m_EventBinding;
        private int m_StateDirty;
        private bool m_Subscribed;
        private AgentUiEvent m_DeferredEvent;
        private bool m_AutoStartSent;

        [Preserve]
        protected override void OnCreate()
        {
            base.OnCreate();

            AgentLoop loop = AgentLoop.EnsureCreated();
            if (!m_Subscribed)
            {
                loop.UiEvent += OnAgentEvent;
                m_Subscribed = true;
            }

            m_StateBinding = new ValueBinding<string>(
                Group,
                "state",
                "{}",
                ValueWriters.Create<string>(),
                System.Collections.Generic.EqualityComparer<string>.Default);
            AddBinding(m_StateBinding);

            m_EventBinding = new EventBinding<string>(Group, "event", ValueWriters.Create<string>());
            AddBinding(m_EventBinding);

            AddBinding(new TriggerBinding<string>(
                Group,
                "send",
                OnSend,
                ValueReaders.Create<string>()));
            AddBinding(new TriggerBinding(Group, "interrupt", OnInterrupt));
            // #region agent log
            AddBinding(new TriggerBinding<string>(
                Group,
                "debugLog",
                OnDebugLog,
                ValueReaders.Create<string>()));
            // #endregion

            PushState();
        }

        private void OnSend(string text)
        {
            if (!string.IsNullOrWhiteSpace(text))
            {
                AgentLoop.EnsureCreated().Send(text);
            }
        }

        private void OnInterrupt()
        {
            AgentLoop.EnsureCreated().Interrupt();
        }

        // #region agent log
        private void OnDebugLog(string json)
        {
            Debug548a1a.LogUiPayload(json ?? "{}");
        }
        // #endregion

        private void OnAgentEvent(AgentUiEvent agentEvent)
        {
            if (agentEvent == null || agentEvent.Kind == "tool")
            {
                return;
            }
            m_Events.Enqueue(agentEvent);
            if (NeedsStateSnapshot(agentEvent))
            {
                Interlocked.Exchange(ref m_StateDirty, 1);
            }
        }

        private static bool NeedsStateSnapshot(AgentUiEvent agentEvent)
        {
            if (agentEvent.Kind == "user" ||
                agentEvent.Kind == "error" ||
                agentEvent.Kind == "turn")
            {
                return true;
            }
            if (agentEvent.Kind != "status")
            {
                return false;
            }
            return agentEvent.Status == AgentStatus.Idle ||
                   agentEvent.Status == AgentStatus.Interrupted ||
                   agentEvent.Status == AgentStatus.Error;
        }

        [Preserve]
        protected override void OnUpdate()
        {
            base.OnUpdate();
            TryAutoStart();
            int processed = 0;
            while (processed < MaxEventsPerUpdate && TryDequeueForUi(out AgentUiEvent agentEvent))
            {
                m_EventBinding.Trigger(agentEvent.ToJsonString());
                processed++;
            }
            if (Interlocked.Exchange(ref m_StateDirty, 0) == 1)
            {
                PushState();
            }
        }

        private void TryAutoStart()
        {
            GameManager manager = GameManager.instance;
            if (manager == null || manager.gameMode != GameMode.Game || manager.isGameLoading)
            {
                m_AutoStartSent = false;
                return;
            }
            if (m_AutoStartSent || !Setting.StaticAutoStart)
            {
                return;
            }

            m_AutoStartSent = true;
            string prompt = Setting.StaticStartupPrompt;
            if (!string.IsNullOrWhiteSpace(prompt))
            {
                AgentLoop.EnsureCreated().Send(prompt);
            }
        }

        private bool TryDequeueForUi(out AgentUiEvent agentEvent)
        {
            if (m_DeferredEvent != null)
            {
                agentEvent = m_DeferredEvent;
                m_DeferredEvent = null;
            }
            else if (!m_Events.TryDequeue(out agentEvent))
            {
                return false;
            }

            if (agentEvent.Kind != "delta")
            {
                return true;
            }

            var text = new StringBuilder(agentEvent.Text ?? "");
            while (m_Events.TryDequeue(out AgentUiEvent next))
            {
                if (next.Kind != "delta")
                {
                    m_DeferredEvent = next;
                    break;
                }
                text.Append(next.Text ?? "");
            }

            agentEvent = new AgentUiEvent
            {
                Kind = "delta",
                Text = text.ToString(),
            };
            return true;
        }

        private void PushState()
        {
            AgentLoop loop = AgentLoop.Instance;
            string json = loop == null ? "{}" : loop.RenderChatStateJson();
            if (m_StateBinding.value != json)
            {
                m_StateBinding.Update(json);
            }
        }

        [Preserve]
        protected override void OnDestroy()
        {
            if (m_Subscribed)
            {
                AgentLoop loop = AgentLoop.Instance;
                if (loop != null)
                {
                    loop.UiEvent -= OnAgentEvent;
                }
                m_Subscribed = false;
            }
            base.OnDestroy();
        }
    }
}
