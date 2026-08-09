namespace CitiesSkylines2Agent.Agent
{
    internal interface IProviderProfile
    {
        string Name { get; }
        bool MatchesEndpoint(string normalizedEndpoint);
        ModelCapabilities Resolve(string modelName);
    }
}
