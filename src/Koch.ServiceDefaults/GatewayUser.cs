using System.Security.Claims;
using System.Text.Encodings.Web;
using Koch.Gateway.Contracts;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Koch.ServiceDefaults;

/// <summary>
/// Who is calling a service: the member the gateway signed in and named in <see cref="ForwardedUser.Header"/>. The
/// gateway has already checked membership; the service believes the header because nothing outside the Container Apps
/// environment can reach it (internal ingress), and the gateway overwrites any copy a caller sent (docs/adr/0002).
/// </summary>
public sealed class GatewayUser(
    IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string Name = "GatewayUser";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var id = Request.Headers[ForwardedUser.Header].ToString();
        if (id.Length == 0) return Task.FromResult(AuthenticateResult.NoResult());

        Claim[] claims = [new(ClaimTypes.NameIdentifier, id)];
        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, Name));
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, Name)));
    }
}

public static class GatewayUserExtensions
{
    /// <summary>Every endpoint that calls <c>RequireAuthorization()</c> then needs a user forwarded by the gateway.</summary>
    public static IServiceCollection AddGatewayUser(this IServiceCollection services)
    {
        services.AddAuthentication(GatewayUser.Name).AddScheme<AuthenticationSchemeOptions, GatewayUser>(GatewayUser.Name, null);
        services.AddAuthorization();
        return services;
    }

    /// <summary>The forwarded user id; only call it on endpoints that require authorization.</summary>
    public static string UserId(this ClaimsPrincipal user) =>
        user.FindFirstValue(ClaimTypes.NameIdentifier) ?? throw new InvalidOperationException("No user was forwarded.");
}
