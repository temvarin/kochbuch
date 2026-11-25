using System.Text.Json;
using System.Text.Json.Serialization;
using Koch.Contracts;
using Koch.Domain.Kernel;
using Koch.Domain.Measures;

namespace Koch.Domain.Shopping;

public static class ShoppingWire
{
    public static ItemJson ToJson(this ShoppingItem item) => new(item.Id, item.Name,
        item.Amount is { } a ? new AmountJson(a.Amount, a.Unit.Code()) : null, item.Checked, item.Recipe);

    public static ShoppingItem ToDomain(this ItemJson json) => new(json.Id, json.Name,
        json.Amount is { } a ? new Quantity(a.Value, Units.FromCode(a.Unit)) : null, json.Checked, json.Recipe);

    public static ShoppingSyncRequestJson ToJson(this ShoppingChanges changes) => new(ShoppingSyncRequestJson.CurrentProtocol,
        new([.. changes.Items.Changed.Select(ToJson)], changes.Items.Removed));

    public static ShoppingChanges ToDomain(this ShoppingSyncRequestJson json)
    {
        var changes = new ShoppingChanges(new([.. json.Items.Changed.Select(ToDomain)], json.Items.Removed));
        if (changes.Items.Changed.DistinctBy(i => i.Id).Count() != changes.Items.Changed.Count)
            throw new ArgumentException("An item appears twice among the changes.");
        return changes;
    }

    public static ShoppingSyncResponseJson ToJson(this ShoppingBook book) => new([.. book.Items.Select(ToJson)]);

    public static ShoppingBook ToDomain(this ShoppingSyncResponseJson json) => new(json.Items.Select(ToDomain));
}

public sealed record StoredShoppingJson(int Schema, ShoppingSyncResponseJson Book, LastShoppingSyncJson? Sync, ShoppingSyncRequestJson? Unconfirmed);

public sealed record LastShoppingSyncJson(DateTimeOffset At, ShoppingSyncResponseJson Base);

[JsonSourceGenerationOptions(JsonSerializerDefaults.Web,
    RespectNullableAnnotations = true, RespectRequiredConstructorParameters = true)]
[JsonSerializable(typeof(StoredShoppingJson))]
internal sealed partial class LocalShoppingJson : JsonSerializerContext;

public sealed class ShoppingStore(IKeyValueStore store)
{
    internal const string Key = "koch.shopping";
    internal const string UnreadableKey = "koch.shopping.unreadable";
    internal const int Schema = 4;

    public SyncedShopping Load()
    {
        if (store.Read(Key) is not { } json) return new SyncedShopping();
        if (TryRead(json) is { } shopping) return shopping;
        if (store.Read(UnreadableKey) is null) store.Write(UnreadableKey, json);
        return new SyncedShopping();
    }

    public void Save(Synced<ShoppingBook, ShoppingChanges> shopping) => store.Write(Key, Serialize(shopping));

    public static string Serialize(Synced<ShoppingBook, ShoppingChanges> shopping) => JsonSerializer.Serialize(
        new StoredShoppingJson(Schema, shopping.Book.ToJson(),
            shopping.State is { } state ? new LastShoppingSyncJson(state.LastSync, state.Base.ToJson()) : null,
            shopping.Unconfirmed?.ToJson()),
        LocalShoppingJson.Default.StoredShoppingJson);

    public static SyncedShopping? TryRead(string json)
    {
        try
        {
            return JsonSerializer.Deserialize(json, LocalShoppingJson.Default.StoredShoppingJson) switch
            {
                { Schema: >= 1 and <= Schema } stored => new SyncedShopping(stored.Book.ToDomain(),
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
