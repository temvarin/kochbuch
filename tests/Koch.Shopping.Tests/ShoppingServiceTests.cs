using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Koch.Gateway.Contracts;
using Koch.Kernel.Measures;
using Koch.ServiceDefaults.Documents;
using Koch.Shopping.Contracts;
using Koch.Shopping.Domain;
using Koch.Shopping.Storage;
using Koch.Testing;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Koch.Shopping.Tests;

/// <summary><c>POST /sync</c> as the gateway forwards it, with the rows in memory.</summary>
public sealed class SyncApiTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly MemoryDocumentRows store = new();

    private HttpClient Service() => factory.WithWebHostBuilder(host =>
    {
        host.UseSetting("ConnectionStrings:Shopping", "Server=unused");
        host.ConfigureTestServices(services => services.AddSingleton<IDocumentRows>(store));
    }).CreateClient();

    private static ShoppingBook List() => ShoppingBook.Empty
        .Add([new PlannedLine("Kartoffeln", new Quantity(500, Unit.Gramm), "Suppe")], () => new Guid("22222222-2222-2222-2222-000000000001"));

    private static async Task<HttpResponseMessage> Post(HttpClient service, HttpContent content, bool forwarded = true)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri("/sync", UriKind.Relative)) { Content = content };
        if (forwarded) request.Headers.Add(ForwardedUser.Header, "member-1");
        return await service.SendAsync(request);
    }

    private static JsonContent Json(ShoppingChanges changes, int? protocol = null) => JsonContent.Create(
        changes.ToJson() with { Protocol = protocol ?? ShoppingSyncRequestJson.CurrentProtocol }, ShoppingJson.Default.ShoppingSyncRequestJson);

    [Fact]
    public async Task The_items_come_back_to_the_next_phone_and_ticks_too()
    {
        var service = Service();
        await Post(service, Json(ShoppingChanges.Between(ShoppingBook.Empty, List())));
        var ticked = List().Check("kartoffeln", true);
        await Post(service, Json(ShoppingChanges.Between(List(), ticked)));

        var response = await Post(service, Json(ShoppingChanges.None));

        var book = (await response.Content.ReadFromJsonAsync(ShoppingJson.Default.ShoppingSyncResponseJson))!.ToDomain();
        Assert.True(Assert.Single(book.Lines()).Checked);
    }

    [Fact]
    public async Task An_app_with_another_protocol_is_asked_to_reload_and_a_broken_item_is_refused()
    {
        Assert.Equal(HttpStatusCode.Conflict, (await Post(Service(), Json(ShoppingChanges.None, protocol: 0))).StatusCode);

        var json = ShoppingChanges.Between(ShoppingBook.Empty, List()).ToJson();
        var broken = json with { Items = json.Items with { Changed = [json.Items.Changed[0] with { Name = "" }] } };
        var response = await Post(Service(), JsonContent.Create(broken, ShoppingJson.Default.ShoppingSyncRequestJson));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(0, store.VersionOf("item:22222222-2222-2222-2222-000000000001"));
    }

    [Fact]
    public async Task Only_json_from_a_forwarded_member_is_accepted()
    {
        Assert.Equal(HttpStatusCode.UnsupportedMediaType,
            (await Post(Service(), new FormUrlEncodedContent([new("protocol", "1")]))).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Post(Service(), Json(ShoppingChanges.None), forwarded: false)).StatusCode);
    }
}

/// <summary>db/shopping against a real SQL Server, through the shared row store.</summary>
public sealed class DatabaseTests : IAsyncLifetime
{
    private readonly SqlDatabase sql = new();

    public Task InitializeAsync() => sql.InitializeAsync();

    public Task DisposeAsync() => sql.DisposeAsync();

    [Fact]
    public async Task The_scripts_make_a_table_the_service_can_use_and_can_run_again()
    {
        var rows = new SqlDocumentRows(sql.ConnectionString, "shopping", NullLogger<SqlDocumentRows>.Instance);
        Assert.False(await rows.CanReadAsync());

        foreach (var script in SqlDatabase.Scripts("shopping")) await sql.ExecuteAsync(script);
        await rows.WriteAndReadAsync([new("item:a", """{"x":1}""")]);
        foreach (var script in SqlDatabase.Scripts("shopping")) await sql.ExecuteAsync(script);

        Assert.True(await rows.CanReadAsync());
        Assert.Single(await rows.WriteAndReadAsync([]));
    }
}

/// <summary>Stored rows are never migrated: this version writes the newest sample and reads every one.</summary>
public class RowContractTests
{
    private static readonly string Folder = Path.Combine(AppContext.BaseDirectory, "Contracts");

    private static ShoppingBook Book() => ShoppingBook.Empty
        .Add([new PlannedLine("Kartoffeln", new Quantity(500, Unit.Gramm), "Kartoffelsuppe")],
            () => new Guid("22222222-2222-2222-2222-000000000001"));

    // "row-list" holds rows of the lists removed by docs/adr/0014: no longer written or read, still in the tables.
    public static TheoryData<string> Names => ["row-item"];

    [Theory, MemberData(nameof(Names))]
    public void This_version_writes_exactly_the_newest_sample_and_reads_it_back(string name)
    {
        var written = Rows.For(ShoppingChanges.Between(ShoppingBook.Empty, Book())).Single().Json!;
        var newest = Directory.Exists(Path.Combine(Folder, name))
            ? Directory.GetFiles(Path.Combine(Folder, name), "v*.json").Order(StringComparer.Ordinal).LastOrDefault()
            : null;
        if (newest is null || !JsonNode.DeepEquals(JsonNode.Parse(File.ReadAllText(newest)), JsonNode.Parse(written)))
        {
            var received = Path.Combine(SourceFolder(), name, "received.json");
            Directory.CreateDirectory(Path.GetDirectoryName(received)!);
            File.WriteAllText(received, JsonNode.Parse(written)!.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
            Assert.Fail($"The service writes {name} differently from its newest sample; see {received}.");
        }

        foreach (var sample in Directory.GetFiles(Path.Combine(Folder, name), "v*.json"))
        {
            var book = Rows.ToBook([new StoredRow("item:x", File.ReadAllText(sample))]);
            Assert.Single(book.Items);
        }
    }

    private static string SourceFolder([System.Runtime.CompilerServices.CallerFilePath] string path = "") =>
        Path.Combine(Path.GetDirectoryName(path)!, "Contracts");
}
