namespace Koch.Gateway.Contracts;

/// <summary>
/// How the gateway tells a service who is asking. The services are reachable only from inside the Container Apps
/// environment (internal ingress), so they believe this header; the gateway removes any copy a caller sent before it
/// sets the real one (docs/adr/0002).
/// </summary>
public static class ForwardedUser
{
    /// <summary>The signed-in member's Entra object id.</summary>
    public const string Header = "X-Koch-User";
}
