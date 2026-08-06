using System.Collections.Concurrent;
using System.Threading;
using Colossal.UI.Binding;
using Game.UI;
using UnityEngine.Scripting;

namespace CitiesSkylines2Agent.Agent
{
    /// <summary>
    /// Bridges the agent loop to Gameface: publishes agent state and a live
    /// event stream (deltas, tool cards, plan cards, status), and receives
    /// player commands (send / approve / interrupt). Registered at UIUpdate so
    /// it keeps running while the simulation is paused.
    /// </summary>
    public sealed partial class AgentUISystem : UISystemBase
    {
        private const string Group = "CitiesSkylines2Agent";

        private readonly ConcurrentQueue<AgentUiEvent> m_Events =
            new ConcurrentQueue<AgentUiEvent>();
        private ValueBinding<string> m_StateBinding;
        private EventBinding<string> m_EventBinding;
        private int m_StateDirty;
        private bool m_Subscribed;

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
            AddBinding(new TriggerBinding(Group, "approve", OnApprove));
            AddBinding(new TriggerBinding(Group, "interrupt", OnInterrupt));

            PushState();
            loop.ResumePlanIfNeeded();
        }

        private void OnSend(string text)
        {
            if (!string.IsNullOrWhiteSpace(text))
            {
                AgentLoop.EnsureCreated().Send(text);
            }
        }

        private void OnApprove()
        {
            AgentLoop.EnsureCreated().ApprovePlan();
        }

        private void OnInterrupt()
        {
            AgentLoop.EnsureCreated().Interrupt();
        }

        private void OnAgentEvent(AgentUiEvent agentEvent)
        {
            m_Events.Enqueue(agentEvent);
            Interlocked.Exchange(ref m_StateDirty, 1);
        }

        [Preserve]
        protected override void OnUpdate()
        {
            base.OnUpdate();
            while (m_Events.TryDequeue(out AgentUiEvent agentEvent))
            {
                m_EventBinding.Trigger(agentEvent.ToJsonString());
                Interlocked.Exchange(ref m_StateDirty, 1);
            }
            if (Interlocked.Exchange(ref m_StateDirty, 0) == 1)
            {
                PushState();
            }
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
