namespace CitiesSkylines2Agent.Agent
{
    /// <summary>
    /// Thin wrapper for ModelCapabilities resolved from per-provider profile files.
    /// Kept for backward compat with existing consumers.
    /// </summary>
    internal sealed class AgentModelProfile
    {
        private readonly ModelCapabilities m_Caps;

        private AgentModelProfile(ModelCapabilities caps)
        {
            m_Caps = caps;
        }

        public long ContextWindowTokens => m_Caps.ContextWindowTokens;
        public long CompactAtTokens => m_Caps.CompactAtTokens;
        public long OutputReserveTokens => m_Caps.OutputReserveTokens;
        public long TailBudgetTokens => m_Caps.TailBudgetTokens;
        public bool SupportsVision => m_Caps.SupportsVision;
        public string Source => m_Caps.Source;

        public static AgentModelProfile Resolve(string endpoint, string model, long fallbackWindowTokens)
        {
            ModelCapabilities caps = ProviderProfileRegistry.Resolve(endpoint, model, fallbackWindowTokens);
            return new AgentModelProfile(caps);
        }
    }
}
