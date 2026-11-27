using System.Net;
using System.Net.Http.Json;
using Koch.Api;
using Koch.Api.Storage;
using Koch.Contracts;
using Koch.Domain.Recipes;
using Koch.Domain.Shopping;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace Koch.App.Tests;

public sealed class MemoryRows : IDocumentRows
{
    private readonly Dictionary<string, (string Json, int Version)> rows = [];
    private readonly Lock gate = new();

    public int VersionOf(string id)
    {
        lock (gate) return rows.TryGetValue(id, out var row) ? row.Version : 0;
    }

    public Task<bool> CanReadAsync(CancellationToken cancellationToken = default) => Task.FromResult(true);

    public Task<IReadOnlyList<StoredRow>> WriteAndReadAsync(IReadOnlyList<RowWrite> writes, CancellationToken cancellationToken = default)
    {
        lock (gate)
        {
            var newRows = new Dictionary<string, (string Json, int Version)>(rows);
            foreach (var write in writes)
            {
                var before = newRows.TryGetValue(write.Id, out var row) ? row.Json : null;
                var json = DocumentRows.Target(write, before);
                if (before == json) continue;
                newRows[write.Id] = (json, before is null ? 1 : row.Version + 1);
            }

            rows.Clear();
            foreach (var (id, row) in newRows) rows[id] = row;
            IReadOnlyList<StoredRow> live = [.. rows.Where(r => r.Value.Json != DocumentRows.Tombstone)
                .OrderBy(r => r.Key, StringComparer.Ordinal).Select(r => new StoredRow(r.Key, r.Value.Json))];
            return Task.FromResult(live);
        }
    }
}

public sealed class AppServer : IDisposable
{
    public static readonly WhoAmI Anna = new("8a1c0e52", "anna@example.com", true);

    private static readonly Uri Address = new("http://localhost/");

    private readonly Lazy<WebApplicationFactory<GatewayUser>> apiHost;
    private readonly Lazy<HttpMessageInvoker> apiClient;

    public AppServer()
    {
        apiHost = new(() => new WebApplicationFactory<GatewayUser>().WithWebHostBuilder(web =>
        {
            web.ConfigureTestServices(s =>
            {
                s.AddKeyedSingleton<IDocumentRows>("recipes", RecipeRows);
                s.AddKeyedSingleton<IDocumentRows>("shopping", ShoppingRows);
            });
        }));
        apiClient = new(() => new HttpMessageInvoker(apiHost.Value.Server.CreateHandler()));
        Network = new NetworkHandler(this);
    }

    public MemoryRows RecipeRows { get; } = new();
    public MemoryRows ShoppingRows { get; } = new();

    public HttpMessageHandler Network { get; }
    public bool Offline { get; set; }
    public WhoAmI? User { get; set; } = Anna;
    public Dictionary<string, (byte[] Bytes, string? BlobType)> Uploaded { get; } = [];
    public bool LoseNextAnswer { get; set; }

    public async Task<RecipeBook> BookAsync() => Koch.Api.Recipes.Rows.ToBook(await RecipeRows.WriteAndReadAsync([]));

    public async Task<ShoppingBook> ListsAsync() => Koch.Api.Shopping.Rows.ToBook(await ShoppingRows.WriteAndReadAsync([]));

    public async Task OnOtherPhone(Func<RecipeBook, RecipeBook> change)
    {
        var changes = BookChanges.Between(RecipeBook.Empty, change(RecipeBook.Empty));
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(Address, "recipes/sync"))
        {
            Content = JsonContent.Create(changes.ToJson(), RecipesJson.Default.SyncRequestJson),
        };
        request.Headers.Add(ForwardedUser.Header, "other-phone");
        using var response = await apiClient.Value.SendAsync(request, CancellationToken.None);
        response.EnsureSuccessStatusCode();
    }

    public void Dispose()
    {
        if (apiClient.IsValueCreated) apiClient.Value.Dispose();
        if (apiHost.IsValueCreated) apiHost.Value.Dispose();
    }

    private sealed class NetworkHandler(AppServer owner) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (owner.Offline) throw new HttpRequestException("No network.");
            var path = request.RequestUri!.AbsolutePath;
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
            if (!path.StartsWith("/api/", StringComparison.Ordinal))
                return new HttpResponseMessage(HttpStatusCode.NotFound);
            if (owner.User is not { Member: true } member) return new HttpResponseMessage(HttpStatusCode.Unauthorized);

            using var forwarded = new HttpRequestMessage(request.Method, new Uri(Address, path["/api/".Length..])) { Content = request.Content };
            forwarded.Headers.Add(ForwardedUser.Header, member.Id);
            var response = await owner.apiClient.Value.SendAsync(forwarded, cancellationToken);
            if (owner.LoseNextAnswer && path.StartsWith("/api/recipes", StringComparison.Ordinal))
            {
                owner.LoseNextAnswer = false;
                response.Dispose();
                throw new HttpRequestException("The answer got lost.");
            }
            return response;
        }
    }
}
