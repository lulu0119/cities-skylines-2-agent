using System;

namespace CitiesSkylines2Agent.Agent
{
    internal sealed class OpenAIProfile : IProviderProfile
    {
        public string Name => "OpenAI";

        public bool MatchesEndpoint(string normalizedEndpoint)
        {
            return normalizedEndpoint.Contains("api.openai.com");
        }

        public ModelCapabilities Resolve(string modelName)
        {
            string model = (modelName ?? "").ToLowerInvariant();

            if (model.StartsWith("gpt-5.6") || model.Contains("gpt-5.6"))
            {
                return new ModelCapabilities
                {
                    ContextWindowTokens = 1_048_576,
                    MaxOutputTokens = 128_000,
                    SupportsVision = true,
                    Source = "openai-gpt-5.6",
                };
            }

            if (model.StartsWith("gpt-4.1") || model.Contains("gpt-4.1"))
            {
                return new ModelCapabilities
                {
                    ContextWindowTokens = 1_047_576,
                    MaxOutputTokens = 32_768,
                    SupportsVision = true,
                    Source = "openai-gpt-4.1",
                };
            }

            if (model.Contains("gpt-4o"))
            {
                return new ModelCapabilities
                {
                    ContextWindowTokens = 128_000,
                    MaxOutputTokens = 16_384,
                    SupportsVision = true,
                    Source = "openai-gpt-4o",
                };
            }

            if (model.Contains("gpt-4.5"))
            {
                return new ModelCapabilities
                {
                    ContextWindowTokens = 128_000,
                    MaxOutputTokens = 16_384,
                    SupportsVision = true,
                    Source = "openai-gpt-4.5",
                };
            }

            if (model.Contains("o1") || model.Contains("o3") || model.Contains("o4") || model.Contains("o1-pro"))
            {
                return new ModelCapabilities
                {
                    ContextWindowTokens = 200_000,
                    MaxOutputTokens = 32_768,
                    SupportsVision = model.Contains("o1") || model.Contains("o4"),
                    Source = "openai-o-series",
                };
            }

            return null;
        }
    }
}