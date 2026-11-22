using System.Security.Claims;
using Koch.Gateway.Account;
using Koch.Gateway.Contracts;
using Yarp.ReverseProxy.Configuration;
using Yarp.ReverseProxy.Transforms;

namespace Koch.Gateway.Proxy;

/// <summary>
/// Forwards <c>/api/{service}/{path}</c> to <c>{service address}/{path}</c>, for members only. The services trust the
/// user named in <see cref="ForwardedUser.Header"/>, so this is where it must be right: whatever the caller sent under
/// that name is dropped, as are Easy Auth's headers and the session cookie, which the services have no use for
/// (docs/adr/0002, the confused deputy).
/// </summary>
public static class ServiceProxy
{
    /// <summary>The services behind the gateway. The address comes from the setting <c>Services:{name}</c>.</summary>
    private static readonly string[] ServiceNames = ["recipes", "shopping"];

    public static IServiceCollection AddServiceProxy(this IServiceCollection services)
    {
        services.AddReverseProxy().AddTransforms(context => context.AddRequestTransform(ForwardUser));
        // Built when first needed, after the configuration is complete (tests add settings late).
        services.AddSingleton<IProxyConfigProvider>(provider =>
        {
            var configuration = provider.GetRequiredService<IConfiguration>();
            return new InMemoryConfigProvider(
                [.. ServiceNames.Select(Route)],
                [.. ServiceNames.Select(name => Cluster(name, configuration))]);
        });
        return services;
    }

    public static void MapServiceProxy(this IEndpointRouteBuilder app) => app.MapReverseProxy();

    private static RouteConfig Route(string name) => new RouteConfig
    {
        RouteId = name,
        ClusterId = name,
        AuthorizationPolicy = Members.Policy,
        Match = new RouteMatch { Path = $"/api/{name}/{{**rest}}" },
    }.WithTransformPathRemovePrefix($"/api/{name}");

    private static ClusterConfig Cluster(string name, IConfiguration configuration) => new()
    {
        ClusterId = name,
        Destinations = new Dictionary<string, DestinationConfig>
        {
            ["main"] = new()
            {
                Address = configuration[$"Services:{name}"]
                    ?? throw new InvalidOperationException($"The setting Services:{name} (the service's address) is missing."),
            },
        },
    };

    private static ValueTask ForwardUser(RequestTransformContext context)
    {
        var headers = context.ProxyRequest.Headers;
        var dropped = headers.Select(header => header.Key)
            .Where(name => name.StartsWith("X-MS-", StringComparison.OrdinalIgnoreCase)
                || name.Equals(ForwardedUser.Header, StringComparison.OrdinalIgnoreCase)
                || name.Equals("Cookie", StringComparison.OrdinalIgnoreCase))
            .ToList();
        foreach (var name in dropped) headers.Remove(name);

        // The route requires the member policy, so a signed-in user is always there.
        var user = context.HttpContext.User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? throw new InvalidOperationException("A proxied request without a signed-in user.");
        headers.Add(ForwardedUser.Header, user);
        return ValueTask.CompletedTask;
    }
}
