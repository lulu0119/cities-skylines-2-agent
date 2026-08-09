using System;

namespace CitiesSkylines2Agent.Agent
{
    internal sealed class OpenRouterProfile : IProviderProfile
    {
        public string Name => "OpenRouter";

        public bool MatchesEndpoint(string normalizedEndpoint)
        {
            return normalizedEndpoint.Contains("openrouter");
        }

        public ModelCapabilities Resolve(string modelName)
        {
            return null;
        }
    }
}
