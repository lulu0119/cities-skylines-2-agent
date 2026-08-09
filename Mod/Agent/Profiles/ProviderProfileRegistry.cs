using System.Collections.Generic;

namespace CitiesSkylines2Agent.Agent
{
    internal static class ProviderProfileRegistry
    {
        private static readonly List<IProviderProfile> s_Profiles = new List<IProviderProfile>
        {
            new OpenAIProfile(),
            new DeepSeekProfile(),
            new OpenRouterProfile(),
        };

        public static IReadOnlyList<IProviderProfile> All => s_Profiles;

        public static ModelCapabilities Resolve(string endpoint, string model, long fallbackWindowTokens)
        {
            string normalizedEndpoint = (endpoint ?? "").ToLowerInvariant();

            // Phase 1: match by endpoint
            IProviderProfile matchedProfile = null;
            foreach (IProviderProfile profile in s_Profiles)
            {
                if (profile.MatchesEndpoint(normalizedEndpoint))
                {
                    matchedProfile = profile;
                    break;
                }
            }

            // Phase 2: try matched profile's model table
            if (matchedProfile != null)
            {
                ModelCapabilities caps = matchedProfile.Resolve(model);
                if (caps != null) return caps;
            }

            // Phase 3: try all other profiles by model name
            foreach (IProviderProfile profile in s_Profiles)
            {
                if (profile == matchedProfile) continue;
                ModelCapabilities caps = profile.Resolve(model);
                if (caps != null) return caps;
            }

            // Phase 4: fallback
            return DefaultProfile.Instance.ResolveFallback(model, fallbackWindowTokens);
        }
    }
}