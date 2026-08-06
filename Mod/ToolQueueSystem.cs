using System;
using System.Collections.Concurrent;
using Colossal.Logging;
using Game;
using UnityEngine.Scripting;

namespace CitiesSkylines2Agent
{
    /// <summary>UIUpdate queue: drain work while paused (CS2MCP-style).</summary>
    public sealed partial class ToolQueueSystem : GameSystemBase
    {
        public static ToolQueueSystem Instance { get; private set; }

        private static readonly ILog Log = LogManager.GetLogger($"{nameof(CitiesSkylines2Agent)}.{nameof(ToolQueueSystem)}").SetShowsErrorsInUI(false);

        private readonly ConcurrentQueue<Action> m_Pending = new ConcurrentQueue<Action>();

        [Preserve]
        protected override void OnCreate()
        {
            base.OnCreate();
            Instance = this;
            Log.Info("ToolQueueSystem created (UIUpdate)");
        }

        public void Enqueue(Action work)
        {
            if (work == null)
            {
                throw new ArgumentNullException(nameof(work));
            }

            m_Pending.Enqueue(work);
        }

        [Preserve]
        protected override void OnUpdate()
        {
            while (m_Pending.TryDequeue(out Action work))
            {
                try
                {
                    work();
                }
                catch (Exception exception)
                {
                    Log.Warn(exception, "ToolQueueSystem work failed");
                }
            }
        }

        [Preserve]
        protected override void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
            }

            base.OnDestroy();
        }
    }
}
