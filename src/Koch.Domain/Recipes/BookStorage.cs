using System.Text.Json;
using System.Text.Json.Serialization;
using Koch.Contracts;
using Koch.Domain.Kernel;
using Koch.Domain.Measures;

namespace Koch.Domain.Recipes;

public static class RecipeWire
{
    public static RecipeJson ToJson(this Recipe recipe) => new(recipe.Id, recipe.Title, recipe.Servings,
        [.. recipe.Lines.Select(l => new LineJson(l.Ingredient, l.Amount?.ToJson(), l.Note))], [.. recipe.Steps], recipe.Notes, recipe.Photo);

    public static Recipe ToDomain(this RecipeJson json) => new(json.Id, json.Title, json.Servings,
        json.Lines.Select(l => new RecipeLine(l.Ingredient, l.Amount?.ToDomain(), l.Note)), json.Steps, json.Notes, json.Photo);

    public static QuantityJson ToJson(this Quantity quantity) => new(quantity.Amount, quantity.Unit.Code());

    public static Quantity ToDomain(this QuantityJson json) => new(json.Value, Units.FromCode(json.Unit));

    public static SyncRequestJson ToJson(this BookChanges changes, IReadOnlyList<Recipe>? bases = null) => new(SyncRequestJson.CurrentProtocol,
        new([.. changes.Recipes.Changed.Select(ToJson)], changes.Recipes.Removed,
            bases is { Count: > 0 } ? [.. bases.Select(ToJson)] : null));

    public static IReadOnlyList<Recipe> BasesToDomain(this SyncRequestJson json)
    {
        List<Recipe> bases = [.. (json.Recipes.Bases ?? []).Select(ToDomain)];
        var changed = json.Recipes.Changed.Select(r => r.Id).ToHashSet();
        if (bases.DistinctBy(r => r.Id).Count() != bases.Count || !bases.All(r => changed.Contains(r.Id)))
            throw new ArgumentException("A base belongs to no changed recipe, or appears twice.");
        return bases;
    }

    public static BookChanges ToDomain(this SyncRequestJson json)
    {
        var changes = new BookChanges(new([.. json.Recipes.Changed.Select(ToDomain)], json.Recipes.Removed));
        if (changes.Recipes.Changed.DistinctBy(r => r.Id).Count() != changes.Recipes.Changed.Count)
            throw new ArgumentException("A recipe appears twice among the changes.");
        return changes;
    }

    public static SyncResponseJson ToJson(this RecipeBook book) => new([.. book.Recipes.Select(ToJson)]);

    public static RecipeBook ToDomain(this SyncResponseJson json) => new(json.Recipes.Select(ToDomain));
}

public sealed record StoredBookJson(int Schema, SyncResponseJson Book, LastSyncJson? Sync, SyncRequestJson? Unconfirmed);

public sealed record LastSyncJson(DateTimeOffset At, SyncResponseJson Base);

[JsonSourceGenerationOptions(JsonSerializerDefaults.Web,
    RespectNullableAnnotations = true, RespectRequiredConstructorParameters = true)]
[JsonSerializable(typeof(StoredBookJson))]
internal sealed partial class LocalJson : JsonSerializerContext;

public sealed class BookStore(IKeyValueStore store)
{
    internal const string Key = "koch.recipes";
    internal const string UnreadableKey = "koch.recipes.unreadable";
    internal const int Schema = 5;

    public SyncedBook Load()
    {
        if (store.Read(Key) is not { } json) return new SyncedBook();
        if (TryRead(json) is { } book) return book;
        if (store.Read(UnreadableKey) is null) store.Write(UnreadableKey, json);
        return new SyncedBook();
    }

    public void Save(Synced<RecipeBook, BookChanges> book) => store.Write(Key, Serialize(book));

    public static string Serialize(Synced<RecipeBook, BookChanges> book) => JsonSerializer.Serialize(
        new StoredBookJson(Schema, book.Book.ToJson(),
            book.State is { } state ? new LastSyncJson(state.LastSync, state.Base.ToJson()) : null,
            book.Unconfirmed?.ToJson()),
        LocalJson.Default.StoredBookJson);

    public static SyncedBook? TryRead(string json)
    {
        try
        {
            return JsonSerializer.Deserialize(json, LocalJson.Default.StoredBookJson) switch
            {
                { Schema: >= 1 and <= Schema } stored => new SyncedBook(stored.Book.ToDomain(),
                    stored.Sync is { } sync ? new SyncState<RecipeBook>(sync.At, sync.Base.ToDomain()) : null,
                    stored.Unconfirmed?.ToDomain()),
                _ => null,
            };
        }
        catch (Exception e) when (e is JsonException or ArgumentException)
        {
            return null;
        }
    }
}
