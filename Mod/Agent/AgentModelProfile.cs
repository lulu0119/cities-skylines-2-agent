namespace CitiesSkylines2Agent.Agent
{
    /// <summary>
    /// Resolved model capabilities after applying explicit player overrides.
    /// </summary>
    internal sealed class AgentModelProfile
    {
        private readonly ModelCapabilities m_Caps;

        private AgentModelProfile(ModelCapabilities caps, VisionToolMode visionMode)
        {
            m_Caps = caps;
            VisionAvailable = visionMode switch
            {
                VisionToolMode.On => true,
                VisionToolMode.Off => false,
                _ => caps.SupportsVision,
            };
        }

        public long ContextWindowTokens => m_Caps.ContextWindowTokens;
        public long CompactAtTokens => m_Caps.CompactAtTokens;
        public long OutputReserveTokens => m_Caps.OutputReserveTokens;
        public long TailBudgetTokens => m_Caps.TailBudgetTokens;
        public bool VisionAvailable { get; }
        public string Source => m_Caps.Source;

        public static AgentModelProfile Resolve(
            string model,
            long fallbackWindowTokens,
            VisionToolMode visionMode,
            ContextBudgetMode contextBudgetMode)
        {
            ModelCapabilities caps = ModelProfileRegistry.Resolve(model, fallbackWindowTokens);
            caps.ContextWindowTokens = ResolveWindowTokens(
                caps.ContextWindowTokens,
                fallbackWindowTokens,
                contextBudgetMode);
            return new AgentModelProfile(caps, visionMode);
        }

        private static long ResolveWindowTokens(
            long profileWindowTokens,
            long customWindowTokens,
            ContextBudgetMode mode)
        {
            if (mode == ContextBudgetMode.Custom)
            {
                return customWindowTokens > 0 ? customWindowTokens : 200_000;
            }

            return profileWindowTokens;
        }
    }
}
