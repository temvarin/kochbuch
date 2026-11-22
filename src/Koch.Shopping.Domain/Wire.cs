using System.Text.Json;
using System.Text.Json.Serialization;
using Koch.Kernel;
using Koch.Kernel.Measures;
using Koch.Shopping.Contracts;

namespace Koch.Shopping.Domain;

/// <summary>
/// Between the domain and the JSON shapes in Koch.Shopping.Contracts. Reading goes through the domain's constructors,
/// so JSON that breaks a rule is refused with an <see cref="ArgumentException"/>.
/// </summary>
public static class Wire
{
    public static ListJson ToJson(this ShoppingList list) => new(list.Id, list.Title, list.Created);

    public static ShoppingList ToDomain(this ListJson json) => new(json.Id, json.Title, json.Created);

    public static ItemJson ToJson(this ShoppingItem item) => new(item.Id, item.ListId, item.Name,
        item.Amount is { } a ? new AmountJson(a.Amount, a.Unit.Code()) : null, item.Aisle.Code(), item.Checked, item.Recipe);

    public static ShoppingItem ToDomain(this ItemJson json) => new(json.Id, json.ListId, json.Name,
        json.Amount is { } a ? new Quantity(a.Value, Units.FromCode(a.Unit)) : null, Aisles.FromCode(json.Aisle),
        json.Checked, json.Recipe);

    public static ShoppingSyncRequestJson ToJson(this ShoppingChanges changes) => new(ShoppingSyncRequestJson.CurrentProtocol,
        new([.. changes.Lists.Changed.Select(ToJson)], changes.Lists.Removed),
        new([.. changes.Items.Changed.Select(ToJson)], changes.Items.Removed));

    /// <exception cref="ArgumentException">An item breaks a rule, or appears twice.</exception>
    public static ShoppingChanges ToDomain(this ShoppingSyncRequestJson json)
    {
        var changes = new ShoppingChanges(
            new([.. json.Lists.Changed.Select(ToDomain)], json.Lists.Removed),
            new([.. json.Items.Changed.Select(ToDomain)], json.Items.Removed));
        if (changes.Lists.Changed.DistinctBy(l => l.Id).Count() != changes.Lists.Changed.Count
            || changes.Items.Changed.DistinctBy(i => i.Id).Count() != changes.Items.Changed.Count)
            throw new ArgumentException("An item appears twice among the changes.");
        return changes;
    }

    public static ShoppingSyncResponseJson ToJson(this ShoppingBook book) =>
        new([.. book.Lists.Select(ToJson)], [.. book.Items.Select(ToJson)]);

    public static ShoppingBook ToDomain(this ShoppingSyncResponseJson json) =>
        new(json.Lists.Select(ToDomain), json.Items.Select(ToDomain));
}

/// <summary>The lists on the phone, with their sync state. See <see cref="ShoppingStore.Schema"/>.</summary>
public sealed record StoredShoppingJson(int Schema, ShoppingSyncResponseJson Book, LastShoppingSyncJson? Sync, ShoppingSyncRequestJson? Unconfirmed);

public sealed record LastShoppingSyncJson(DateTimeOffset At, ShoppingSyncResponseJson Base);

// Nulls are written: reading requires every constructor parameter (the lesson of the recipe store, ADR-0004).
[JsonSourceGenerationOptions(JsonSerializerDefaults.Web,
    RespectNullableAnnotations = true, RespectRequiredConstructorParameters = true)]
[JsonSerializable(typeof(StoredShoppingJson))]
internal sealed partial class LocalShoppingJson : JsonSerializerContext;

/// <summary>Saves the <see cref="SyncedShopping"/> under its own key, apart from the recipe book.</summary>
public sealed class ShoppingStore(IKeyValueStore store)
{
    internal const string Key = "koch.shopping";
    internal const string UnreadableKey = "koch.shopping.unreadable";

    /// <summary>1 since 2026-11-22.</summary>
    internal const int Schema = 1;

    public SyncedShopping Load()
    {
        if (store.Read(Key) is not { } json) return new SyncedShopping();
        if (TryRead(json) is { } shopping) return shopping;
        if (store.Read(UnreadableKey) is null) store.Write(UnreadableKey, json);
        return new SyncedShopping();
    }

    public void Save(SyncedShopping shopping) => store.Write(Key, Serialize(shopping));

    public static string Serialize(SyncedShopping shopping) => JsonSerializer.Serialize(
        new StoredShoppingJson(Schema, shopping.Book.ToJson(),
            shopping.State is { } state ? new LastShoppingSyncJson(state.LastSync, state.Base.ToJson()) : null,
            shopping.Unconfirmed?.ToJson()),
        LocalShoppingJson.Default.StoredShoppingJson);

    /// <returns>null if it isn't ours, comes from an unknown version, or breaks the rules.</returns>
    public static SyncedShopping? TryRead(string json)
    {
        try
        {
            return JsonSerializer.Deserialize(json, LocalShoppingJson.Default.StoredShoppingJson) switch
            {
                { Schema: Schema } stored => new SyncedShopping(stored.Book.ToDomain(),
                    stored.Sync is { } sync ? new SyncState<ShoppingBook>(sync.At, sync.Base.ToDomain()) : null,
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
