namespace CS2MCP
{
    /// <summary>
    /// Simulation-thread request handler seam. The built-in implementation is
    /// always available; development builds may replace it between requests.
    /// </summary>
    public interface IRequestHandlerAdapter
    {
        BridgeResponse Handle(BridgeRequest request);
    }
}
