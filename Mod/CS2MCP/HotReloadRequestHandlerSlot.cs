using System;
using System.IO;
using System.Reflection;
using CitiesSkylines2Agent.Agent;

namespace CS2MCP
{
    /// <summary>
    /// Keeps the bridge's request-handler interface stable while swapping a
    /// development adapter loaded from outside the watched mod directory.
    /// Invalid payloads leave the last known-good adapter active.
    /// </summary>
    internal sealed class HotReloadRequestHandlerSlot : IRequestHandlerAdapter
    {
        private const string HandlerTypeName = "CS2MCP.RequestHandlers";
        private const int MaxReloadsPerGameSession = 32;

        private readonly BridgeSystem m_System;
        private readonly IRequestHandlerAdapter m_Builtin;
        private IRequestHandlerAdapter m_Current;
        private DateTime m_LastWriteUtc;
        private long m_LastLength = -1;
        private bool m_OverrideActive;
        private int m_ReloadCount;
        private bool m_LimitLogged;

        public HotReloadRequestHandlerSlot(
            BridgeSystem system,
            IRequestHandlerAdapter builtin)
        {
            m_System = system;
            m_Builtin = builtin;
            m_Current = builtin;
        }

        public BridgeResponse Handle(BridgeRequest request)
        {
            TryReload();
            return m_Current.Handle(request);
        }

        private void TryReload()
        {
            string path = ModPaths.HotReloadHandlersFile;
            if (!File.Exists(path))
            {
                if (m_OverrideActive)
                {
                    m_Current = m_Builtin;
                    m_OverrideActive = false;
                    m_LastWriteUtc = default;
                    m_LastLength = -1;
                    Mod.Log.Info("hot-reload handler payload removed; restored built-in handlers");
                }
                return;
            }

            var file = new FileInfo(path);
            bool unchanged = file.LastWriteTimeUtc == m_LastWriteUtc && file.Length == m_LastLength;
            if (unchanged)
            {
                return;
            }

            if (m_ReloadCount >= MaxReloadsPerGameSession)
            {
                if (!m_LimitLogged)
                {
                    m_LimitLogged = true;
                    Mod.Log.Warn(
                        "hot-reload limit reached (" + MaxReloadsPerGameSession +
                        "); restart the game before loading another handler payload");
                }
                return;
            }

            m_LastWriteUtc = file.LastWriteTimeUtc;
            m_LastLength = file.Length;
            try
            {
                // Loading from bytes avoids locking the payload, so the next
                // build can replace it while the game remains open.
                Assembly assembly = Assembly.Load(File.ReadAllBytes(path));
                Type handlerType = assembly.GetType(HandlerTypeName, throwOnError: true);
                if (!typeof(IRequestHandlerAdapter).IsAssignableFrom(handlerType))
                {
                    throw new InvalidOperationException(
                        HandlerTypeName + " does not implement " + nameof(IRequestHandlerAdapter));
                }

                var replacement = (IRequestHandlerAdapter)Activator.CreateInstance(
                    handlerType,
                    m_System);
                m_Current = replacement;
                m_OverrideActive = true;
                m_ReloadCount++;
                Mod.Log.Info(
                    "hot-reloaded request handlers " + m_ReloadCount + "/" +
                    MaxReloadsPerGameSession + " " +
                    assembly.ManifestModule.ModuleVersionId.ToString("N"));
            }
            catch (Exception e)
            {
                Mod.Log.Warn(
                    "hot-reload handler payload rejected; keeping last known-good handlers: " +
                    e.Message);
            }
        }
    }
}
