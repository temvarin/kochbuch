extern alias gateway;
extern alias recipes;
extern alias shopping;

using System.Net;
using System.Net.Sockets;
using Koch.ServiceDefaults.Documents;
using Koch.Testing;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;
using Yarp.ReverseProxy.Forwarder;

namespace Koch.E2E;

/// <summary>
/// The whole system on this machine, as the two phones of the household see it:
/// <list type="bullet">
/// <item>the real gateway on a real port, serving the real Blazor app and checking sign-in as behind Easy Auth;</item>
/// <item>Recipes and Shopping in-process with their memory stores, reached through the gateway's proxy;</item>
/// <item>a browser: the installed Edge locally, Playwright's Chromium in CI (PLAYWRIGHT_CHANNEL set empty).</item>
/// </list>
/// Each phone is a browser context of its own, so its local storage is its own, as on a real phone.
/// </summary>
public sealed class Household : IAsyncLifetime, IAsyncDisposable
{
    public const string Anna = "a11a0000-0000-4000-8000-00000000anna", Ben = "be110000-0000-4000-8000-000000000ben";

    private readonly WebApplicationFactory<recipes::Program> recipesService = new RecipesService();
    private readonly WebApplicationFactory<shopping::Program> shoppingService = new ShoppingService();
    private GatewayApp? gatewayApp;
    private IPlaywright? playwright;
    private IBrowser? browser;

    public string Url { get; private set; } = "";

    public async Task InitializeAsync()
    {
        var port = FreePort();
        gatewayApp = new GatewayApp(recipesService.Server.CreateHandler(), shoppingService.Server.CreateHandler());
        gatewayApp.UseKestrel(port);
        gatewayApp.StartServer();
        Url = $"http://localhost:{port}/";

        playwright = await Playwright.CreateAsync();
        var channel = Environment.GetEnvironmentVariable("PLAYWRIGHT_CHANNEL") ?? "msedge";
        browser = await playwright.Chromium.LaunchAsync(new() { Channel = channel.Length == 0 ? null : channel });
    }

    /// <summary>A fresh phone for one member: empty local storage, phone-sized, German.</summary>
    public async Task<Phone> PhoneAsync(string member)
    {
        var context = await browser!.NewContextAsync(new()
        {
            BaseURL = Url,
            ViewportSize = new() { Width = 390, Height = 844 },
            Locale = "de-DE",
            // What Easy Auth puts on every request after sign-in (Koch.Gateway.Account.EasyAuth).
            ExtraHTTPHeaders = new Dictionary<string, string>
            {
                ["X-MS-CLIENT-PRINCIPAL-ID"] = member,
                ["X-MS-CLIENT-PRINCIPAL-NAME"] = $"{member}@example.com",
            },
            // A worker would answer from its cache; these tests are about the network and sync.
            ServiceWorkers = ServiceWorkerPolicy.Block,
        });
        return new Phone(context, await context.NewPageAsync());
    }

    public async Task DisposeAsync()
    {
        if (browser is not null) await browser.DisposeAsync();
        playwright?.Dispose();
        if (gatewayApp is not null) await gatewayApp.DisposeAsync();
        await recipesService.DisposeAsync();
        await shoppingService.DisposeAsync();
    }

    ValueTask IAsyncDisposable.DisposeAsync() => new(DisposeAsync());

    private static int FreePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }

    private sealed class RecipesService : WebApplicationFactory<recipes::Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseSetting("ConnectionStrings:Recipes", "Server=unused");
            builder.ConfigureTestServices(services =>
                services.AddSingleton<IDocumentRows>(new MemoryDocumentRows(recipes::Koch.Recipes.Storage.Rows.PhotoEvents)));
        }
    }

    private sealed class ShoppingService : WebApplicationFactory<shopping::Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseSetting("ConnectionStrings:Shopping", "Server=unused");
            builder.ConfigureTestServices(services => services.AddSingleton<IDocumentRows>(new MemoryDocumentRows()));
        }
    }

    /// <summary>The gateway as in Azure, except that its proxy reaches the services in-process.</summary>
    private sealed class GatewayApp(HttpMessageHandler recipes, HttpMessageHandler shopping) : WebApplicationFactory<gateway::Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseSetting("Koch:EasyAuth", "true");
            builder.UseSetting("Koch:Members", $"{Anna},{Ben}");
            builder.UseSetting("Services:recipes", "http://recipes");
            builder.UseSetting("Services:shopping", "http://shopping");
            builder.ConfigureTestServices(services => services.AddSingleton<IForwarderHttpClientFactory>(new InProcess(recipes, shopping)));
        }
    }

    private sealed class InProcess(HttpMessageHandler recipes, HttpMessageHandler shopping) : HttpMessageHandler, IForwarderHttpClientFactory
    {
        private readonly HttpMessageInvoker toRecipes = new(recipes, disposeHandler: false);
        private readonly HttpMessageInvoker toShopping = new(shopping, disposeHandler: false);

        public HttpMessageInvoker CreateClient(ForwarderHttpClientContext context) => new(this, disposeHandler: false);

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            (request.RequestUri!.Host == "recipes" ? toRecipes : toShopping).SendAsync(request, cancellationToken);
    }
}

[CollectionDefinition(Name)]
public sealed class UsesHousehold : ICollectionFixture<Household>
{
    public const string Name = "household";
}
