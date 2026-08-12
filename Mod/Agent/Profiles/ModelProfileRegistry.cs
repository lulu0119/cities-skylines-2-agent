using System.Collections.Generic;

namespace CitiesSkylines2Agent.Agent
{
    internal static class ModelProfileRegistry
    {
        private static readonly List<IModelProfile> s_Profiles = new List<IModelProfile>
        {
            new OpenAIProfile(),
            new DeepSeekProfile(),
        };

        public static ModelCapabilities Resolve(string model, long fallbackWindowTokens)
        {
            foreach (IModelProfile profile in s_Profiles)
            {
                ModelCapabilities caps = profile.Resolve(model);
                if (caps != null)
                {
                    return caps;
                }
            }

            return DefaultProfile.Instance.ResolveFallback(fallbackWindowTokens);
        }
    }
}
