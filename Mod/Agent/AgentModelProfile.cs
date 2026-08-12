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
            VisionToolMode visionMode)
        {
            ModelCapabilities caps = ModelProfileRegistry.Resolve(model, fallbackWindowTokens);
            return new AgentModelProfile(caps, visionMode);
        }
    }
}
