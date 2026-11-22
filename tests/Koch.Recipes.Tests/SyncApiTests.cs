using System.Net;
using System.Net.Http.Json;
using Koch.Gateway.Contracts;
using Koch.Kernel;
using Koch.Kernel.Measures;
using Koch.Recipes.Contracts;
using Koch.Recipes.Domain.Cooking;
using Koch.Recipes.Domain.Sync;
using Koch.Recipes.Domain.Wire;
using Koch.Recipes.Events;
using Koch.Recipes.Storage;
using Koch.ServiceDefaults.Documents;
using Koch.Testing;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace Koch.Recipes.Tests;

/// <summary><c>POST /sync</c> as the gateway forwards it, with the rows in memory.</summary>
public sealed class SyncApiTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    private static readonly Guid SoupId = new("6f1d1c2a-7b4e-4c55-9d2b-1e0b5a6c7d80");

    private readonly MemoryDocumentRows store = new(Rows.PhotoEvents);

    private HttpClient Service() => factory.WithWebHostBuilder(host =>
    {
        host.UseSetting("ConnectionStrings:Recipes", "Server=unused");
        host.ConfigureTestServices(services => services.AddSingleton<IDocumentRows>(store));
    }).CreateClient();

    private static Recipe Soup(string title = "Kartoffelsuppe", Guid? photo = null) =>
        new(SoupId, title, 4, [new RecipeLine("Kartoffeln", new Quantity(500, Unit.Gramm))], ["Kochen."], photo: photo);

    private static BookChanges Saved(Recipe recipe) => BookChanges.Between(RecipeBook.Empty, RecipeBook.Empty.Save(recipe));

    private static async Task<HttpResponseMessage> Post(HttpClient service, HttpContent content, string? traceParent = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri("/sync", UriKind.Relative)) { Content = content };
        request.Headers.Add(ForwardedUser.Header, "member-1");
        if (traceParent is not null) request.Headers.Add("traceparent", traceParent);
        return await service.SendAsync(request);
    }

    private static Task<HttpResponseMessage> Post(HttpClient service, SyncRequestJson json) =>
        Post(service, JsonContent.Create(json, RecipesJson.Default.SyncRequestJson));

    private static async Task<RecipeBook> Book(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync(RecipesJson.Default.SyncResponseJson))!.ToDomain();
    }

    [Fact]
    public async Task A_saved_recipe_and_its_ingredients_come_back_to_the_next_phone()
    {
        var service = Service();
        await Post(service, Saved(Soup()).ToJson());

        var book = await Book(await Post(service, BookChanges.None.ToJson()));

        Assert.Equal(Soup(), Assert.Single(book.Recipes));
        Assert.Equal("Kartoffeln", Assert.Single(book.Ingredients).Name);
    }

    [Fact]
    public async Task A_removed_recipe_is_gone_for_everyone()
    {
        var service = Service();
        await Post(service, Saved(Soup()).ToJson());

        var book = await Book(await Post(service,
            new BookChanges(new([], [SoupId]), ItemChanges.None<Koch.Recipes.Domain.Catalog.Ingredient>()).ToJson()));

        Assert.Empty(book.Recipes);
    }

    [Fact]
    public async Task The_same_request_twice_saves_once()
    {
        var service = Service();

        await Post(service, Saved(Soup()).ToJson());
        await Post(service, Saved(Soup()).ToJson()); // e.g. the answer got lost and the phone sends it again

        Assert.Equal(1, store.VersionOf($"recipe:{SoupId}"));
    }

    [Fact]
    public async Task An_app_with_another_protocol_is_asked_to_reload()
    {
        var response = await Post(Service(), Saved(Soup()).ToJson() with { Protocol = 0 });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(0, store.VersionOf($"recipe:{SoupId}"));
    }

    [Fact]
    public async Task A_recipe_that_breaks_a_rule_is_refused_and_nothing_is_saved()
    {
        var json = Saved(Soup()).ToJson();
        var broken = json with { Recipes = json.Recipes with { Changed = [json.Recipes.Changed[0] with { Servings = 0 }] } };

        var response = await Post(Service(), broken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(0, store.VersionOf($"recipe:{SoupId}"));
    }

    [Theory]
    [InlineData("{ not json", "application/json")]
    [InlineData("""{"protocol":1}""", "application/json")]
    public async Task Json_that_is_no_sync_request_is_refused(string body, string type)
    {
        var response = await Post(Service(), new StringContent(body, System.Text.Encoding.UTF8, type));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Only_json_is_accepted_against_forged_form_posts()
    {
        var response = await Post(Service(), new FormUrlEncodedContent([new("protocol", "1")]));

        Assert.Equal(HttpStatusCode.UnsupportedMediaType, response.StatusCode);
    }

    [Fact]
    public async Task Without_a_forwarded_user_nothing_is_saved()
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri("/sync", UriKind.Relative))
        {
            Content = JsonContent.Create(Saved(Soup()).ToJson(), RecipesJson.Default.SyncRequestJson),
        };

        var response = await Service().SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(0, store.VersionOf($"recipe:{SoupId}"));
    }

    [Fact]
    public async Task A_photo_saved_with_a_recipe_is_announced_once_and_its_replacement_too()
    {
        var service = Service();
        var first = new Guid("11111111-1111-4111-8111-111111111111");
        var second = new Guid("22222222-2222-4222-8222-222222222222");
        var withPhoto = Soup(photo: first);
        await Post(service, Saved(withPhoto).ToJson());
        await Post(service, Saved(withPhoto).ToJson()); // sent again: no second event

        var book = RecipeBook.Empty.Save(withPhoto);
        await Post(service, BookChanges.Between(book, book.Save(Soup(photo: second))).ToJson());

        Assert.Equal(
            [(PhotoEvents.Attached, PhotoEvents.Subject(first)), (PhotoEvents.Detached, PhotoEvents.Subject(first)),
             (PhotoEvents.Attached, PhotoEvents.Subject(second))],
            store.Events.Select(e => (e.Type, e.Subject)));
    }

    [Fact]
    public async Task A_photo_event_carries_the_trace_of_the_sync_request_that_caused_it()
    {
        // As the gateway forwards it: trace 0af7…, the gateway's span b7ad….
        const string gateway = "00-0af7651916cd43dd8448eb211c80319c-b7ad6b7169203331-01";

        await Post(Service(), JsonContent.Create(Saved(Soup(photo: Guid.NewGuid())).ToJson(), RecipesJson.Default.SyncRequestJson), gateway);

        var saved = Assert.Single(await store.ReadUnsentAsync(10)).TraceParent;
        Assert.NotNull(saved);
        Assert.StartsWith("00-0af7651916cd43dd8448eb211c80319c-", saved, StringComparison.Ordinal);
        Assert.NotEqual(gateway, saved); // the service's own span in that trace, a child of the gateway's
    }

    [Fact]
    public async Task Two_phones_editing_different_parts_of_one_recipe_both_keep_their_edit()
    {
        var service = Service();
        var soup = Soup();
        await Post(service, Saved(soup).ToJson());
        var book = RecipeBook.Empty.Save(soup);

        // Both phones start from the same soup. Ben adds pepper and syncs first; Anna, offline meanwhile, renames it.
        var bens = new Recipe(SoupId, soup.Title, 4, [.. soup.Lines, new RecipeLine("Pfeffer", null)], soup.Steps);
        await Post(service, BookChanges.Between(book, book.Save(bens)).ToJson([soup]));
        var annas = new Recipe(SoupId, "Annas Kartoffelsuppe", 4, soup.Lines, soup.Steps);
        var merged = await Book(await Post(service, BookChanges.Between(book, book.Save(annas)).ToJson([soup])));

        var result = Assert.Single(merged.Recipes);
        Assert.Equal("Annas Kartoffelsuppe", result.Title);
        Assert.Equal(["Kartoffeln", "Pfeffer"], result.Lines.Select(l => l.Ingredient));
    }

    [Fact]
    public async Task Without_a_base_the_recipe_that_arrives_last_wins_whole()
    {
        var service = Service();
        var soup = Soup();
        await Post(service, Saved(soup).ToJson());
        var book = RecipeBook.Empty.Save(soup);

        await Post(service, BookChanges.Between(book, book.Save(new Recipe(SoupId, soup.Title, 4, [.. soup.Lines, new RecipeLine("Pfeffer", null)], soup.Steps))).ToJson());
        var merged = await Book(await Post(service, BookChanges.Between(book, book.Save(new Recipe(SoupId, "Annas Kartoffelsuppe", 4, soup.Lines, soup.Steps))).ToJson()));

        Assert.Equal(["Kartoffeln"], Assert.Single(merged.Recipes).Lines.Select(l => l.Ingredient));
    }

    [Fact]
    public async Task A_base_for_a_recipe_that_is_not_changed_is_refused()
    {
        var other = new Recipe(Guid.NewGuid(), "Anderes", 2, [], []);

        var response = await Post(Service(), BookChanges.Between(RecipeBook.Empty, RecipeBook.Empty.Save(Soup())).ToJson([other]));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
