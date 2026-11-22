using System.Net;
using System.Net.Http.Json;
using Koch.Gateway.Contracts;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Microsoft.Net.Http.Headers;
using Yarp.ReverseProxy.Forwarder;

namespace Koch.Gateway.Tests;

/// <summary>
/// The gateway behind Easy Auth, which passes the signed-in user on in request headers, in front of the services,
/// which believe the user the gateway names (docs/adr/0002).
/// </summary>
public sealed class GatewayTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>, IDisposable
{
    private const string Member = "8a1c0e52-member";
    private const string Stranger = "f39b77d0-stranger";

    private readonly FakeServices services = new();
    private readonly FakeTimeProvider clock = new(new DateTimeOffset(2026, 11, 22, 9, 0, 0, TimeSpan.Zero));

    public void Dispose() => services.Dispose();

    private HttpClient Gateway(bool easyAuth = true) => factory.WithWebHostBuilder(host =>
    {
        host.UseSetting("Koch:Members", $"{Member}, other-member");
        host.UseSetting("Services:recipes", "http://kochbuch-recipes");
        host.UseSetting("Services:shopping", "http://kochbuch-shopping");
        if (easyAuth) host.UseSetting("Koch:EasyAuth", "true");
        host.ConfigureTestServices(test =>
        {
            test.AddSingleton<TimeProvider>(clock);
            test.AddSingleton<IForwarderHttpClientFactory>(services);
        });
    }).CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false, AllowAutoRedirect = false });

    private static async Task<HttpResponseMessage> Get(
        HttpClient gateway, string path, string? user, Action<HttpRequestMessage>? extra = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(path, UriKind.Relative));
        if (user is not null)
        {
            request.Headers.Add("X-MS-CLIENT-PRINCIPAL-ID", user);
            request.Headers.Add("X-MS-CLIENT-PRINCIPAL-NAME", $"{user}@example.com");
        }
        extra?.Invoke(request);
        return await gateway.SendAsync(request);
    }

    [Theory]
    [InlineData("/api/whoami")]
    [InlineData("/api/recipes/status")]
    [InlineData("/api/nothing")]
    public async Task The_api_asks_for_sign_in_rather_than_returning_the_app(string path)
    {
        var response = await Get(Gateway(), path, user: null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Empty(services.Received);
    }

    [Theory]
    [InlineData(Member, true)]
    [InlineData(Stranger, false)]
    public async Task A_signed_in_user_learns_their_id_and_whether_they_are_a_member(string user, bool member)
    {
        var response = await Get(Gateway(), "/api/whoami", user);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(new WhoAmI(user, $"{user}@example.com", member),
            await response.Content.ReadFromJsonAsync(GatewayJson.Default.WhoAmI));
    }

    [Fact]
    public async Task A_stranger_never_reaches_a_service()
    {
        var response = await Get(Gateway(), "/api/recipes/status", Stranger);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Empty(services.Received);
    }

    [Fact]
    public async Task A_members_request_reaches_the_service_without_the_prefix_and_names_the_member()
    {
        var response = await Get(Gateway(), "/api/recipes/status?x=1", Member);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var received = Assert.Single(services.Received);
        Assert.Equal("http://kochbuch-recipes/status?x=1", received.Url);
        Assert.Equal([Member], received.Header(ForwardedUser.Header));
    }

    [Fact]
    public async Task Each_service_gets_its_own_requests()
    {
        await Get(Gateway(), "/api/shopping/sync", Member);

        Assert.Equal("http://kochbuch-shopping/sync", Assert.Single(services.Received).Url);
    }

    [Fact]
    public async Task A_user_header_sent_by_the_caller_is_replaced_by_the_signed_in_member()
    {
        await Get(Gateway(), "/api/recipes/status", Member,
            request => request.Headers.Add(ForwardedUser.Header, ["someone-else", "another"]));

        Assert.Equal([Member], Assert.Single(services.Received).Header(ForwardedUser.Header));
    }

    [Fact]
    public async Task Easy_auth_headers_and_the_session_cookie_stay_at_the_gateway()
    {
        await Get(Gateway(), "/api/recipes/status", Member, request =>
        {
            request.Headers.Add("Cookie", "AppServiceAuthSession=secret");
            request.Headers.Add("X-MS-TOKEN-AAD-ID-TOKEN", "token");
        });

        var received = Assert.Single(services.Received);
        Assert.DoesNotContain(received.Headers.Keys, name => name.StartsWith("X-MS-", StringComparison.OrdinalIgnoreCase));
        Assert.Empty(received.Header("Cookie"));
    }

    [Fact]
    public async Task Without_easy_auth_the_principal_headers_are_not_believed()
    {
        var response = await Get(Gateway(easyAuth: false), "/api/recipes/status", Member);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Empty(services.Received);
    }

    [Fact]
    public async Task Unknown_api_routes_are_not_found_for_members()
    {
        var response = await Get(Gateway(), "/api/nothing", Member);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Api_answers_carry_the_security_headers_and_are_not_stored()
    {
        var response = await Get(Gateway(), "/api/whoami", Member);

        Assert.Contains("script-src 'self' 'wasm-unsafe-eval'", response.Headers.GetValues("Content-Security-Policy").Single());
        Assert.Equal("DENY", response.Headers.GetValues("X-Frame-Options").Single());
        Assert.True(response.Headers.CacheControl?.NoStore);
    }

    [Fact]
    public async Task The_platforms_health_probe_needs_no_sign_in()
    {
        var response = await Get(Gateway(), "/healthz", user: null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task With_application_insights_configured_the_gateway_starts_and_records_traces()
    {
        // A key nobody owns and an endpoint that refuses: the exporter fails quietly in the background, as it must.
        using var gateway = factory.WithWebHostBuilder(host =>
        {
            host.UseSetting("APPLICATIONINSIGHTS_CONNECTION_STRING",
                "InstrumentationKey=00000000-0000-0000-0000-000000000000;IngestionEndpoint=http://127.0.0.1:9/");
            host.UseSetting("Koch:Members", Member);
            host.UseSetting("Services:recipes", "http://kochbuch-recipes");
            host.UseSetting("Services:shopping", "http://kochbuch-shopping");
        });

        var response = await gateway.CreateClient().GetAsync(new Uri("/healthz", UriKind.Relative));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(gateway.Services.GetService<OpenTelemetry.Trace.TracerProvider>());
    }

    [Fact]
    public async Task Whoami_gives_the_session_cookie_an_expiry_date()
    {
        var response = await Get(Gateway(), "/api/whoami", Member,
            request => request.Headers.Add("Cookie", "AppServiceAuthSession=abc%2Bdef"));

        var cookie = SetCookieHeaderValue.ParseList([.. response.Headers.GetValues("Set-Cookie")]).Single();
        Assert.Equal("abc%2Bdef", cookie.Value.ToString());
        Assert.Equal(clock.GetUtcNow() + TimeSpan.FromDays(30), cookie.Expires);
    }
}
