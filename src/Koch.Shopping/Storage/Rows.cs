using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Koch.ServiceDefaults.Documents;
using Koch.Shopping.Contracts;
using Koch.Shopping.Domain;

namespace Koch.Shopping.Storage;

/// <summary>
/// The rows' JSON, kept apart from the API's serializer: a change to the API must not change stored rows, which are
/// never migrated (tests/Koch.Shopping.Tests/Contracts, "row-list", "row-item").
/// </summary>
[JsonSourceGenerationOptions(JsonSerializerDefaults.Web,
    RespectNullableAnnotations = true, RespectRequiredConstructorParameters = true)]
[JsonSerializable(typeof(ListJson))]
[JsonSerializable(typeof(ItemJson))]
public sealed partial class ShoppingRowJson : JsonSerializerContext;

/// <summary>Between the shopping lists and their rows: one row per list and per item.</summary>
public static class Rows
{
    private const string ListPrefix = "list:", ItemPrefix = "item:";

    public static IReadOnlyList<RowWrite> For(ShoppingChanges changes) =>
    [
        .. changes.Lists.Changed.Select(l => new RowWrite(ListPrefix + l.Id, JsonSerializer.Serialize(l.ToJson(), ShoppingRowJson.Default.ListJson))),
        .. changes.Lists.Removed.Select(id => new RowWrite(ListPrefix + id, null)),
        .. changes.Items.Changed.Select(i => new RowWrite(ItemPrefix + i.Id, JsonSerializer.Serialize(i.ToJson(), ShoppingRowJson.Default.ItemJson))),
        .. changes.Items.Removed.Select(id => new RowWrite(ItemPrefix + id, null)),
    ];

    /// <exception cref="JsonException">A row is not what this service writes.</exception>
    public static ShoppingBook ToBook(IEnumerable<StoredRow> rows)
    {
        var list = rows.ToList();
        return new ShoppingBook(
            list.Where(r => r.Id.StartsWith(ListPrefix, StringComparison.Ordinal)).Select(r => Read(r, ShoppingRowJson.Default.ListJson).ToDomain()),
            list.Where(r => r.Id.StartsWith(ItemPrefix, StringComparison.Ordinal)).Select(r => Read(r, ShoppingRowJson.Default.ItemJson).ToDomain()));
    }

    private static T Read<T>(StoredRow row, JsonTypeInfo<T> type) =>
        JsonSerializer.Deserialize(row.Json, type) ?? throw new JsonException($"Row {row.Id} is null.");
}
