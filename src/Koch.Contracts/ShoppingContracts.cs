using System.Text.Json;
using System.Text.Json.Serialization;

namespace Koch.Contracts;

public sealed record AmountJson(decimal Value, string Unit);

public sealed record ItemJson(Guid Id, string Name, AmountJson? Amount, bool Checked, string? Recipe);

public sealed record ItemChangesJson(IReadOnlyList<ItemJson> Changed, IReadOnlyList<Guid> Removed);

public sealed record ShoppingSyncRequestJson(int Protocol, ItemChangesJson Items)
{
    public const int CurrentProtocol = 4;
}

public sealed record ShoppingSyncResponseJson(IReadOnlyList<ItemJson> Items);

[JsonSourceGenerationOptions(JsonSerializerDefaults.Web,
    RespectNullableAnnotations = true, RespectRequiredConstructorParameters = true)]
[JsonSerializable(typeof(ShoppingSyncRequestJson))]
[JsonSerializable(typeof(ShoppingSyncResponseJson))]
public sealed partial class ShoppingJson : JsonSerializerContext;
