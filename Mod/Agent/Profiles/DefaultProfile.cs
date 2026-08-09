using System;

namespace CitiesSkylines2Agent.Agent
{
    internal sealed class DefaultProfile : IProviderProfile
    {
        public static readonly DefaultProfile Instance = new DefaultProfile();

        public string Name => "Default";

        public bool MatchesEndpoint(string normalizedEndpoint) => false;

        public ModelCapabilities Resolve(string modelName) => null;

        public ModelCapabilities ResolveFallback(string modelName, long fallbackWindowTokens)
        {
            long context = Math.Max(16_000, fallbackWindowTokens > 0 ? fallbackWindowTokens : 200_000);
            return new ModelCapabilities
            {
                ContextWindowTokens = context,
                MaxOutputTokens = Math.Min(16_384, Math.Max(4_096, context / 10)),
                SupportsVision = !LooksLikeKnownNonVision(modelName),
                Source = "default",
            };
        }

        private static bool LooksLikeKnownNonVision(string model)
        {
            string m = (model ?? "").ToLowerInvariant();
            if (m.StartsWith("deepseek")) return true;
            if (m.Contains("instruct") && !m.Contains("vision")) return true;
            return false;
        }
    }
}