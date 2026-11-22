using System.Net;
using System.Net.Http.Json;
using Koch.Gateway.Contracts;
using Koch.Recipes.Contracts;
using Koch.Recipes.Domain.Sync;
using Koch.Recipes.Domain.Wire;
using Koch.ServiceDefaults.Documents;
using Koch.Shopping.Domain;
using Koch.Testing;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace Koch.App.Tests;

/// <summary>
/// What the app talks to: a stand-in for the gateway (sign-in, forwarding with the member's id) in front of the real
/// Koch.Recipes and Koch.Shopping services, running in memory with their rows in <see cref="MemoryDocumentRows"/>. In
/// between, the network, with what a test lets go wrong there. Modelled on kassensturz's AppServer.
/// </summary>
public sealed class AppServer : IDisposable
{
    public static readonly WhoAmI Anna = new("8a1c0e52", "anna@example.com", true);

    private static readonly Uri Address = new("http://localhost/");

    private readonly Lazy<WebApplicationFactory<Koch.Recipes.Storage.RowJson>> recipesHost;
    private readonly Lazy<WebApplicationFactory<Koch.Shopping.Storage.ShoppingRowJson>> shoppingHost;
    private readonly Dictionary<string, Lazy<HttpMessageInvoker>> services;

    public AppServer()
    {
        // Any type of a service names its assembly; the factory starts that assembly's Program.
        recipesHost = new(() => new WebApplicationFactory<Koch.Recipes.Storage.RowJson>().WithWebHostBuilder(web =>
        {
            web.UseSetting("ConnectionStrings:Recipes", "Server=unused");
            web.ConfigureTestServices(s => s.AddSingleton<IDocumentRows>(RecipeRows));
        }));
        shoppingHost = new(() => new WebApplicationFactory<Koch.Shopping.Storage.ShoppingRowJson>().WithWebHostBuilder(web =>
        {
            web.UseSetting("ConnectionStrings:Shopping", "Server=unused");
            web.ConfigureTestServices(s => s.AddSingleton<IDocumentRows>(ShoppingRows));
        }));
        services = new()
        {
            ["recipes"] = new(() => new HttpMessageInvoker(recipesHost.Value.Server.CreateHandler())),
            ["shopping"] = new(() => new HttpMessageInvoker(shoppingHost.Value.Server.CreateHandler())),
        };
        Network = new NetworkHandler(this);
    }

    public MemoryDocumentRows RecipeRows { get; } = new();

    public MemoryDocumentRows ShoppingRows { get; } = new();

    /// <summary>The app's way to the gateway.</summary>
    public HttpMessageHandler Network { get; }

    /// <summary>No network at all: every request fails.</summary>
    public bool Offline { get; set; }

    /// <summary>Who the gateway's Easy Auth says is signed in; null: nobody.</summary>
    public WhoAmI? User { get; set; } = Anna;

    /// <summary>Photos the phone put into "Blob Storage", by blob path, with the blob type it said.</summary>
    public Dictionary<string, (byte[] Bytes, string? BlobType)> Uploaded { get; } = [];

    /// <summary>The next recipe sync reaches the service and is saved, but its answer gets lost on the way back.</summary>
    public bool LoseNextAnswer { get; set; }

    /// <summary>The book as the Recipes service has stored it.</summary>
    public async Task<RecipeBook> BookAsync() => Koch.Recipes.Storage.Rows.ToBook(await RecipeRows.WriteAndReadAsync([]));

    /// <summary>The lists as the Shopping service has stored them.</summary>
    public async Task<ShoppingBook> ListsAsync() => Koch.Shopping.Storage.Rows.ToBook(await ShoppingRows.WriteAndReadAsync([]));

    /// <summary>A recipe change made on another phone and synced from there.</summary>
    public async Task OnOtherPhone(Func<RecipeBook, RecipeBook> change)
    {
        var changes = BookChanges.Between(RecipeBook.Empty, change(RecipeBook.Empty));
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(Address, "sync"))
        {
            Content = JsonContent.Create(changes.ToJson(), RecipesJson.Default.SyncRequestJson),
        };
        request.Headers.Add(ForwardedUser.Header, "other-phone");
        using var response = await services["recipes"].Value.SendAsync(request, CancellationToken.None);
        response.EnsureSuccessStatusCode();
    }

    public void Dispose()
    {
        foreach (var service in services.Values.Where(s => s.IsValueCreated)) service.Value.Dispose();
        if (recipesHost.IsValueCreated) recipesHost.Value.Dispose();
        if (shoppingHost.IsValueCreated) shoppingHost.Value.Dispose();
    }

    private sealed class NetworkHandler(AppServer owner) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (owner.Offline) throw new HttpRequestException("No network.");
            var path = request.RequestUri!.AbsolutePath;
            // Blob Storage, reached with the upload link the gateway handed out.
            if (request.RequestUri.Host == "blob.test" && request.Method == HttpMethod.Put)
            {
                owner.Uploaded[path] = (await request.Content!.ReadAsByteArrayAsync(cancellationToken),
                    request.Headers.TryGetValues("x-ms-blob-type", out var type) ? type.Single() : null);
                return new HttpResponseMessage(HttpStatusCode.Created);
            }
            if (path == "/api/photos/upload-slot")
            {
                var photo = Guid.NewGuid();
                var slot = new UploadSlot(photo, new Uri($"https://blob.test/uploads/{photo}?sig=test"), DateTimeOffset.UnixEpoch, 15 * 1024 * 1024);
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(slot, GatewayJson.Default.UploadSlot) };
            }
            if (path == "/api/whoami")
            {
                return owner.User is { } user
                    ? new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(user, GatewayJson.Default.WhoAmI) }
                    : new HttpResponseMessage(HttpStatusCode.Unauthorized);
            }
            var parts = path.Split('/', 4); // "", "api", service, rest
            if (parts.Length < 4 || parts[1] != "api" || !owner.services.TryGetValue(parts[2], out var service))
                return new HttpResponseMessage(HttpStatusCode.NotFound);
            if (owner.User is not { Member: true } member) return new HttpResponseMessage(HttpStatusCode.Unauthorized);

            // What the gateway does: drop the prefix, name the member.
            using var forwarded = new HttpRequestMessage(request.Method, new Uri(Address, parts[3])) { Content = request.Content };
            forwarded.Headers.Add(ForwardedUser.Header, member.Id);
            var response = await service.Value.SendAsync(forwarded, cancellationToken);
            if (owner.LoseNextAnswer && parts[2] == "recipes")
            {
                owner.LoseNextAnswer = false;
                response.Dispose();
                throw new HttpRequestException("The answer got lost.");
            }
            return response;
        }
    }
}
