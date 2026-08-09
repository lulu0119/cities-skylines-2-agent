using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading.Tasks;
using Colossal.Serialization.Entities;
using Game;
using Game.Simulation;
using UnityEngine.Scripting;

namespace CS2MCP
{
    /// <summary>
    /// ECS system that owns the in-process tool bridge. Tool calls arrive from
    /// the agent loop on background threads, get queued here, and are executed
    /// in OnUpdate on the simulation main thread (the only place ECS access is
    /// safe). Registered at SystemUpdatePhase.UIUpdate so it keeps running
    /// while paused.
    /// </summary>
    public sealed partial class BridgeSystem : GameSystemBase
    {
        public static BridgeSystem Instance { get; private set; }

        private RequestHandlers m_Handlers;
        private SimulationSystem m_SimulationSystem;
        private uint m_FrameIndexAtLoad;
        private float m_WaitRestoreSpeed = -1f;
        private readonly ConcurrentQueue<BridgeRequest> m_Pending = new ConcurrentQueue<BridgeRequest>();

        /// <summary>
        /// False until the simulation has advanced at least one frame after the
        /// current save finished loading. While false, unlock replay (Locked
        /// component removal, tax parameter unlocks...) may not have run yet,
        /// so lock states read from ECS can be stale.
        /// </summary>
        public bool SimulationHasTickedSinceLoad { get; private set; } = true;

        /// <summary>Frame at which a timed wait ends (0 = no wait active).</summary>
        public uint AutoPauseTargetFrame { get; private set; }

        /// <summary>
        /// Starts a timed simulation run: at targetFrame the simulation speed
        /// is restored to <paramref name="restoreSpeed"/> (0 = paused).
        /// </summary>
        public void StartTimedRun(uint targetFrame, float restoreSpeed)
        {
            AutoPauseTargetFrame = targetFrame;
            m_WaitRestoreSpeed = restoreSpeed;
        }

        [Preserve]
        protected override void OnCreate()
        {
            base.OnCreate();
            Instance = this;
            m_Handlers = new RequestHandlers(this);
            m_SimulationSystem = base.World.GetOrCreateSystemManaged<SimulationSystem>();
        }

        [Preserve]
        protected override void OnGameLoadingComplete(Purpose purpose, GameMode mode)
        {
            base.OnGameLoadingComplete(purpose, mode);
            m_FrameIndexAtLoad = m_SimulationSystem.frameIndex;
            SimulationHasTickedSinceLoad = false;
        }

        [Preserve]
        protected override void OnUpdate()
        {
            if (!SimulationHasTickedSinceLoad && m_SimulationSystem.frameIndex != m_FrameIndexAtLoad)
            {
                SimulationHasTickedSinceLoad = true;
            }
            if (AutoPauseTargetFrame != 0 && m_SimulationSystem.frameIndex >= AutoPauseTargetFrame)
            {
                m_SimulationSystem.selectedSpeed = m_WaitRestoreSpeed >= 0f
                    ? m_WaitRestoreSpeed
                    : 0f;
                AutoPauseTargetFrame = 0;
                m_WaitRestoreSpeed = -1f;
                Mod.Log.Info("timed wait finished, simulation state restored");
            }
            while (m_Pending.TryDequeue(out BridgeRequest request))
            {
                BridgeResponse response;
                try
                {
                    // A null response means the handler completes the request
                    // asynchronously itself (e.g. screenshots at end-of-frame).
                    response = m_Handlers.Handle(request);
                }
                catch (Exception e)
                {
                    Mod.Log.Warn($"error handling {request.Path}: {e}");
                    response = BridgeResponse.Error(500, $"{e.GetType().Name}: {e.Message}");
                }
                if (response != null)
                {
                    request.Complete(response);
                }
            }
        }

        /// <summary>
        /// In-process tool invocation: safe from any thread. The request runs
        /// on the simulation main thread; the returned task completes when the
        /// handler (and any multi-frame tool pipeline) is done.
        /// </summary>
        public Task<BridgeResponse> InvokeAsync(string path, IReadOnlyDictionary<string, string> query = null, string body = null)
        {
            var request = new BridgeRequest
            {
                Method = "GET",
                Path = path,
                Body = body ?? string.Empty,
            };
            if (query != null)
            {
                foreach (KeyValuePair<string, string> pair in query)
                {
                    request.Query[pair.Key] = pair.Value;
                }
            }
            m_Pending.Enqueue(request);
            return request.CompletionTask;
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
