using System;

namespace CitiesSkylines2Agent.Agent
{
    internal sealed class DefaultProfile
    {
        public static readonly DefaultProfile Instance = new DefaultProfile();

        public ModelCapabilities ResolveFallback(long fallbackWindowTokens)
        {
            long context = Math.Max(16_000, fallbackWindowTokens > 0 ? fallbackWindowTokens : 200_000);
            return new ModelCapabilities
            {
                ContextWindowTokens = context,
                MaxOutputTokens = Math.Min(16_384, Math.Max(4_096, context / 10)),
                SupportsVision = false,
                Source = "default",
            };
        }
    }
}
