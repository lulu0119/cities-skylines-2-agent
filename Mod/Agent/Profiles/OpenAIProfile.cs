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
                // Sol / Terra / Luna �� ȫϵ 1.05M context / 128K output / ֧Ԯ vision
                // https://developers.openai.com/api/docs/models/gpt-5.6-sol
                return new ModelCapabilities
                {
                    ContextWindowTokens = 1_050_000,
                    MaxOutputTokens = 128_000,
                    SupportsVision = true,
                    Source = "openai-gpt-5.6",
                };
            }

            return null;
        }
    }
}