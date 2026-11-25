using System.Security.Claims;
using Koch.Contracts;
using Koch.Gateway.Account;
using Yarp.ReverseProxy.Configuration;
using Yarp.ReverseProxy.Transforms;

namespace Koch.Gateway.Proxy;

/// <summary>
/// Forwards <c>/api/recipes/*</c> and <c>/api/shopping/*</c> to the internal <c>Koch.Api</c> service,
/// stripping any caller-supplied user/Easy-Auth/cookie headers and setting <see cref="ForwardedUser.Header"/>.
/// </summary>
public static class ServiceProxy
{
    private const string ClusterId = "api";

    public static IServiceCollection AddServiceProxy(this IServiceCollection services)
    {
        services.AddReverseProxy().AddTransforms(context => context.AddRequestTransform(ForwardUser));
        services.AddSingleton<IProxyConfigProvider>(provider =>
        {
            var configuration = provider.GetRequiredService<IConfiguration>();
            var address = configuration["Services:Api"]
                ?? throw new InvalidOperationException("The setting Services:Api (the backend address) is missing.");
            return new InMemoryConfigProvider(
                [Route("recipes"), Route("shopping")],
                [
                    new ClusterConfig
                    {
                        ClusterId = ClusterId,
                        Destinations = new Dictionary<string, DestinationConfig>
                        {
                            ["main"] = new() { Address = address },
                        },
                    },
                ]);
        });
        return services;
    }

    public static void MapServiceProxy(this IEndpointRouteBuilder app) => app.MapReverseProxy();

    private static RouteConfig Route(string area) => new RouteConfig
    {
        RouteId = area,
        ClusterId = ClusterId,
        AuthorizationPolicy = Members.Policy,
        Match = new RouteMatch { Path = $"/api/{area}/{{**rest}}" },
    }.WithTransformPathRemovePrefix("/api");

    private static ValueTask ForwardUser(RequestTransformContext context)
    {
        var headers = context.ProxyRequest.Headers;
        var dropped = headers.Select(header => header.Key)
            .Where(name => name.StartsWith("X-MS-", StringComparison.OrdinalIgnoreCase)
                || name.Equals(ForwardedUser.Header, StringComparison.OrdinalIgnoreCase)
                || name.Equals("Cookie", StringComparison.OrdinalIgnoreCase))
            .ToList();
        foreach (var name in dropped) headers.Remove(name);

        var user = context.HttpContext.User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? throw new InvalidOperationException("A proxied request without a signed-in user.");
        headers.Add(ForwardedUser.Header, user);
        return ValueTask.CompletedTask;
    }
}
