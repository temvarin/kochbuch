using System.Net;
using System.Net.Http.Json;
using Koch.Api;
using Koch.Api.Storage;
using Koch.Contracts;
using Koch.Domain.Measures;
using Koch.Domain.Recipes;
using Koch.Domain.Shopping;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace Koch.Services.Tests;

public sealed class ApiTests(WebApplicationFactory<GatewayUser> factory) : IClassFixture<WebApplicationFactory<GatewayUser>>
{
    private static readonly Guid SoupId = new("6f1d1c2a-7b4e-4c55-9d2b-1e0b5a6c7d80");

    private readonly MemoryDocumentRows recipeStore = new();
    private readonly MemoryDocumentRows shoppingStore = new();

    private sealed class FakeDatabase(bool readable) : IDocumentRows
    {
        public Task<bool> CanReadAsync(CancellationToken cancellationToken = default) => Task.FromResult(readable);
        public Task<IReadOnlyList<StoredRow>> WriteAndReadAsync(IReadOnlyList<RowWrite> writes, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<StoredRow>>([]);
    }

    private HttpClient Service(bool? overrideReadable = null) => factory.WithWebHostBuilder(host =>
    {
        host.UseSetting("ConnectionStrings:Database", "Server=unused");
        host.ConfigureTestServices(services =>
        {
            services.AddKeyedSingleton<IDocumentRows>("recipes", overrideReadable is { } r ? new FakeDatabase(r) : recipeStore);
            services.AddKeyedSingleton<IDocumentRows>("shopping", overrideReadable is { } s ? new FakeDatabase(s) : shoppingStore);
        });
    }).CreateClient();

    private static Recipe Soup(string title = "Kartoffelsuppe") =>
        new(SoupId, title, 4, [new RecipeLine("Kartoffeln", new Quantity(500, Unit.Gramm))], ["Kochen."]);

    private static BookChanges Saved(Recipe recipe) => BookChanges.Between(RecipeBook.Empty, RecipeBook.Empty.Save(recipe));

    private static ShoppingBook ShoppingList() => ShoppingBook.Empty
        .Add([new PlannedLine("Kartoffeln", new Quantity(500, Unit.Gramm), "Suppe")], () => new Guid("22222222-2222-2222-2222-000000000001"));

    private static async Task<HttpResponseMessage> Post(HttpClient service, string path, HttpContent content, bool forwarded = true)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(path, UriKind.Relative)) { Content = content };
        if (forwarded) request.Headers.Add(ForwardedUser.Header, "member-1");
        return await service.SendAsync(request);
    }

    private static async Task<RecipeBook> ReadRecipes(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync(RecipesJson.Default.SyncResponseJson))!.ToDomain();
    }

    [Fact]
    public async Task A_request_that_did_not_come_through_the_gateway_is_refused()
    {
        var response = await Service().GetAsync(new Uri("/recipes/status", UriKind.Relative));
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);

        var sync = await Post(Service(), "/recipes/sync", JsonContent.Create(Saved(Soup()).ToJson(), RecipesJson.Default.SyncRequestJson), forwarded: false);
        Assert.Equal(HttpStatusCode.Unauthorized, sync.StatusCode);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Status_names_the_forwarded_member_and_whether_the_database_answers(bool readable)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri("/recipes/status", UriKind.Relative));
        request.Headers.Add(ForwardedUser.Header, "member-1");
        var response = await Service(readable).SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(new RecipesStatus("member-1", readable),
            await response.Content.ReadFromJsonAsync(RecipesJson.Default.RecipesStatus));
    }

    [Fact]
    public async Task The_health_probe_needs_no_user()
    {
        var response = await Service().GetAsync(new Uri("/healthz", UriKind.Relative));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task A_saved_recipe_comes_back_and_a_removed_one_is_gone()
    {
        var service = Service();
        await Post(service, "/recipes/sync", JsonContent.Create(Saved(Soup()).ToJson(), RecipesJson.Default.SyncRequestJson));
        await Post(service, "/recipes/sync", JsonContent.Create(Saved(Soup()).ToJson(), RecipesJson.Default.SyncRequestJson));
        Assert.Equal(1, recipeStore.VersionOf($"recipe:{SoupId}"));

        var book = await ReadRecipes(await Post(service, "/recipes/sync",
            JsonContent.Create(BookChanges.None.ToJson(), RecipesJson.Default.SyncRequestJson)));
        Assert.Equal(Soup(), Assert.Single(book.Recipes));

        var afterRemove = await ReadRecipes(await Post(service, "/recipes/sync",
            JsonContent.Create(new BookChanges(new([], [SoupId])).ToJson(), RecipesJson.Default.SyncRequestJson)));
        Assert.Empty(afterRemove.Recipes);
    }

    [Fact]
    public async Task Recipes_protocol_mismatch_broken_rules_and_non_json_are_refused()
    {
        var service = Service();
        Assert.Equal(HttpStatusCode.Conflict,
            (await Post(service, "/recipes/sync", JsonContent.Create(Saved(Soup()).ToJson() with { Protocol = 0 }, RecipesJson.Default.SyncRequestJson))).StatusCode);
        Assert.Equal(HttpStatusCode.UnsupportedMediaType,
            (await Post(service, "/recipes/sync", new FormUrlEncodedContent([new("protocol", "1")]))).StatusCode);

        var json = Saved(Soup()).ToJson();
        var broken = json with { Recipes = json.Recipes with { Changed = [json.Recipes.Changed[0] with { Servings = 0 }] } };
        Assert.Equal(HttpStatusCode.BadRequest,
            (await Post(service, "/recipes/sync", JsonContent.Create(broken, RecipesJson.Default.SyncRequestJson))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await Post(service, "/recipes/sync", new StringContent("{ not json", System.Text.Encoding.UTF8, "application/json"))).StatusCode);
        Assert.Equal(0, recipeStore.VersionOf($"recipe:{SoupId}"));
    }

    [Fact]
    public async Task Two_phones_editing_different_parts_of_one_recipe_both_keep_their_edit()
    {
        var service = Service();
        var soup = Soup();
        await Post(service, "/recipes/sync", JsonContent.Create(Saved(soup).ToJson(), RecipesJson.Default.SyncRequestJson));
        var book = RecipeBook.Empty.Save(soup);

        var bens = new Recipe(SoupId, soup.Title, 4, [.. soup.Lines, new RecipeLine("Pfeffer", null)], soup.Steps);
        await Post(service, "/recipes/sync", JsonContent.Create(BookChanges.Between(book, book.Save(bens)).ToJson([soup]), RecipesJson.Default.SyncRequestJson));

        var annas = new Recipe(SoupId, "Annas Kartoffelsuppe", 4, soup.Lines, soup.Steps);
        var merged = await ReadRecipes(await Post(service, "/recipes/sync",
            JsonContent.Create(BookChanges.Between(book, book.Save(annas)).ToJson([soup]), RecipesJson.Default.SyncRequestJson)));

        var result = Assert.Single(merged.Recipes);
        Assert.Equal("Annas Kartoffelsuppe", result.Title);
        Assert.Equal(["Kartoffeln", "Pfeffer"], result.Lines.Select(l => l.Ingredient));
    }

    [Fact]
    public async Task Shopping_items_sync_and_ticks_propagate_while_bad_requests_are_refused()
    {
        var service = Service();
        var list = ShoppingList();
        await Post(service, "/shopping/sync", JsonContent.Create(ShoppingChanges.Between(ShoppingBook.Empty, list).ToJson(), ShoppingJson.Default.ShoppingSyncRequestJson));
        var ticked = list.Check("kartoffeln", true);
        await Post(service, "/shopping/sync", JsonContent.Create(ShoppingChanges.Between(list, ticked).ToJson(), ShoppingJson.Default.ShoppingSyncRequestJson));

        var response = await Post(service, "/shopping/sync", JsonContent.Create(ShoppingChanges.None.ToJson(), ShoppingJson.Default.ShoppingSyncRequestJson));
        var book = (await response.Content.ReadFromJsonAsync(ShoppingJson.Default.ShoppingSyncResponseJson))!.ToDomain();
        Assert.True(Assert.Single(book.Lines()).Checked);

        Assert.Equal(HttpStatusCode.Conflict,
            (await Post(service, "/shopping/sync", JsonContent.Create(ShoppingChanges.None.ToJson() with { Protocol = 0 }, ShoppingJson.Default.ShoppingSyncRequestJson))).StatusCode);
        Assert.Equal(HttpStatusCode.UnsupportedMediaType,
            (await Post(service, "/shopping/sync", new FormUrlEncodedContent([new("protocol", "1")]))).StatusCode);

        var broken = ShoppingChanges.Between(ShoppingBook.Empty, list).ToJson();
        broken = broken with { Items = broken.Items with { Changed = [broken.Items.Changed[0] with { Name = "" }] } };
        Assert.Equal(HttpStatusCode.BadRequest,
            (await Post(service, "/shopping/sync", JsonContent.Create(broken, ShoppingJson.Default.ShoppingSyncRequestJson))).StatusCode);
    }
}
