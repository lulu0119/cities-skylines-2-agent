namespace CitiesSkylines2Agent.Agent
{
    internal sealed class DeepSeekProfile : IProviderProfile
    {
        public string Name => "DeepSeek";

        public bool MatchesEndpoint(string normalizedEndpoint)
        {
            return normalizedEndpoint.Contains("deepseek");
        }

        public ModelCapabilities Resolve(string modelName)
        {
            string model = (modelName ?? "").ToLowerInvariant();

            if (model.StartsWith("deepseek-v4") || model.Contains("deepseek-v4"))
            {
                // Flash / Pro ¡ª 1M context / ×î´ó 384K output / Ÿo vision
                // https://api-docs.deepseek.com/quick_start/pricing
                return new ModelCapabilities
                {
                    ContextWindowTokens = 1_000_000,
                    MaxOutputTokens = 384_000,
                    SupportsVision = false,
                    Source = "deepseek-v4",
                };
            }

            if (model.StartsWith("deepseek") || model.Contains("deepseek"))
            {
                return new ModelCapabilities
                {
                    ContextWindowTokens = 128_000,
                    MaxOutputTokens = 8_192,
                    SupportsVision = false,
                    Source = "deepseek-v3",
                };
            }

            return null;
        }
    }
}