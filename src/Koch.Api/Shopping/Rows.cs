using System.Text.Json;
using System.Text.Json.Serialization;
using Koch.Api.Storage;
using Koch.Contracts;
using Koch.Domain.Shopping;

namespace Koch.Api.Shopping;

[JsonSourceGenerationOptions(JsonSerializerDefaults.Web,
    RespectNullableAnnotations = true, RespectRequiredConstructorParameters = true)]
[JsonSerializable(typeof(ItemJson))]
public sealed partial class ShoppingRowJson : JsonSerializerContext;

public static class Rows
{
    private const string ItemPrefix = "item:";

    public static IReadOnlyList<RowWrite> For(ShoppingChanges changes) =>
    [
        .. changes.Items.Changed.Select(i => new RowWrite(ItemPrefix + i.Id, JsonSerializer.Serialize(i.ToJson(), ShoppingRowJson.Default.ItemJson))),
        .. changes.Items.Removed.Select(id => new RowWrite(ItemPrefix + id, null)),
    ];

    public static ShoppingBook ToBook(IEnumerable<StoredRow> rows) => new(rows
        .Where(r => r.Id.StartsWith(ItemPrefix, StringComparison.Ordinal))
        .Select(r => (JsonSerializer.Deserialize(r.Json, ShoppingRowJson.Default.ItemJson)
            ?? throw new JsonException($"Row {r.Id} is null.")).ToDomain()));
}
