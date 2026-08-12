namespace CitiesSkylines2Agent.Agent
{
    internal interface IModelProfile
    {
        ModelCapabilities Resolve(string modelName);
    }
}
