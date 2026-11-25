using System.Text.Json;
using Koch.Contracts;

namespace Koch.Domain.Tests;

public class SerializationTests
{
    private sealed class MemoryStore : IKeyValueStore
    {
        private readonly Dictionary<string, string> values = [];
        public string? Read(string key) => values.GetValueOrDefault(key);
        public void Write(string key, string value) => values[key] = value;
    }

    [Fact]
    public void Recipe_book_and_sync_payloads_round_trip_without_loss()
    {
        var store = new MemoryStore();
        var bookStore = new BookStore(store);
        var phone = new SyncedBook();
        phone.Change(b => b.Save(Known.Soup()));
        phone.CompleteSync(phone.StartSync(), Known.Book(), Known.Now);
        phone.Change(b => b.Remove(Known.SoupId));
        phone.SyncFailed(phone.StartSync());

        bookStore.Save(phone);
        var loaded = bookStore.Load();

        Assert.Equal(phone.Book.Recipes, loaded.Book.Recipes);
        Assert.Equal(phone.State?.LastSync, loaded.State?.LastSync);
        Assert.Equal(phone.State?.Base.Recipes, loaded.State?.Base.Recipes);
        Assert.Equal(phone.Unconfirmed?.Recipes.Removed, loaded.Unconfirmed?.Recipes.Removed);

        var reqJson = JsonSerializer.Serialize(
            BookChanges.Between(RecipeBook.Empty, Known.Book()).ToJson([Known.Soup()]),
            RecipesJson.Default.SyncRequestJson);
        var req = JsonSerializer.Deserialize(reqJson, RecipesJson.Default.SyncRequestJson)!;
        Assert.Single(req.ToDomain().Recipes.Changed);
        Assert.Single(req.BasesToDomain());
    }

    [Fact]
    public void Shopping_book_and_sync_payloads_round_trip_without_loss()
    {
        var store = new MemoryStore();
        var shoppingStore = new ShoppingStore(store);
        var phone = new SyncedShopping();
        phone.Change(_ => Known.List());
        phone.CompleteSync(phone.StartSync(), Known.List(), Known.Now);
        phone.Change(b => b.Check("kartoffeln", true));
        phone.SyncFailed(phone.StartSync());

        shoppingStore.Save(phone);
        var loaded = shoppingStore.Load();

        Assert.Equal(phone.Book.Items, loaded.Book.Items);
        Assert.Equal(phone.State?.LastSync, loaded.State?.LastSync);
        Assert.Equal(phone.Unconfirmed?.Items.Changed, loaded.Unconfirmed?.Items.Changed);

        var reqJson = JsonSerializer.Serialize(
            ShoppingChanges.Between(ShoppingBook.Empty, Known.List()).ToJson(),
            ShoppingJson.Default.ShoppingSyncRequestJson);
        var req = JsonSerializer.Deserialize(reqJson, ShoppingJson.Default.ShoppingSyncRequestJson)!;
        Assert.Equal(3, req.ToDomain().Items.Changed.Count);
    }

    [Fact]
    public void Legacy_extra_fields_are_ignored_when_reading_stored_books()
    {
        var legacyRecipe = $$"""
            {
              "schema": 1,
              "book": {
                "recipes": [{"id":"{{Known.SoupId}}","title":"Suppe","servings":2,"lines":[{"ingredient":"Salz","amount":null,"note":""}],"steps":[],"notes":""}],
                "ingredients": [{"id":"00000000-0000-0000-0000-000000000001","name":"Salz","aisle":"sonstiges"}]
              },
              "sync": null,
              "unconfirmed": null
            }
            """;
        Assert.Single(BookStore.TryRead(legacyRecipe)!.Book.Recipes);

        var legacyShopping = """
            {
              "schema": 1,
              "book": {
                "lists": [{"id":"00000000-0000-0000-0000-000000000001","name":"Woche"}],
                "items": [{"id":"22222222-2222-2222-2222-000000000001","listId":"00000000-0000-0000-0000-000000000001","name":"Salz","aisle":"sonstiges","amount":null,"checked":false,"recipe":null}]
              },
              "sync": null,
              "unconfirmed": null
            }
            """;
        Assert.Single(ShoppingStore.TryRead(legacyShopping)!.Book.Items);
    }
}
