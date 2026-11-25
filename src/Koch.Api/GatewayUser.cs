using System.Security.Claims;
using System.Text.Encodings.Web;
using Koch.Contracts;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace Koch.Api;

/// <summary>
/// Authenticates internal requests forwarded by <c>Koch.Gateway</c> via <see cref="ForwardedUser.Header"/>.
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
    public static IServiceCollection AddGatewayUser(this IServiceCollection services)
    {
        services.AddAuthentication(GatewayUser.Name).AddScheme<AuthenticationSchemeOptions, GatewayUser>(GatewayUser.Name, null);
        services.AddAuthorization();
        return services;
    }

    public static string UserId(this ClaimsPrincipal user) =>
        user.FindFirstValue(ClaimTypes.NameIdentifier) ?? throw new InvalidOperationException("No user was forwarded.");
}
