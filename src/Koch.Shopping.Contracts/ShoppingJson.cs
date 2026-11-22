using System.Text.Json;
using System.Text.Json.Serialization;

namespace Koch.Shopping.Contracts;

// The shapes of POST /api/shopping/sync. Versioned by ShoppingSyncRequestJson.Protocol; samples of every released
// version are in tests/Koch.Shopping.Domain.Tests/Contracts.

/// <param name="Unit">"g", "kg", "ml", "l", "TL", "EL", "Stück", "Dose", …</param>
public sealed record AmountJson(decimal Value, string Unit);

public sealed record ListJson(Guid Id, string Title, DateTimeOffset Created);

/// <param name="Amount">null: no amount.</param>
/// <param name="Aisle">"obst-gemuese", "kuehlregal", …</param>
/// <param name="Recipe">The recipe it is for; null if added by hand.</param>
public sealed record ItemJson(Guid Id, Guid ListId, string Name, AmountJson? Amount, string Aisle, bool Checked, string? Recipe);

public sealed record ListChangesJson(IReadOnlyList<ListJson> Changed, IReadOnlyList<Guid> Removed);

public sealed record ItemChangesJson(IReadOnlyList<ItemJson> Changed, IReadOnlyList<Guid> Removed);

public sealed record ShoppingSyncRequestJson(int Protocol, ListChangesJson Lists, ItemChangesJson Items)
{
    /// <summary>The service answers 409 to any other, and the app asks to be reloaded.</summary>
    public const int CurrentProtocol = 1;
}

/// <summary>All lists and items as the service has them after applying the request: the phone's new base.</summary>
public sealed record ShoppingSyncResponseJson(IReadOnlyList<ListJson> Lists, IReadOnlyList<ItemJson> Items);

/// <summary>The serializer for the Shopping API, generated at build time (no reflection in the browser).</summary>
[JsonSourceGenerationOptions(JsonSerializerDefaults.Web,
    RespectNullableAnnotations = true, RespectRequiredConstructorParameters = true)]
[JsonSerializable(typeof(ShoppingSyncRequestJson))]
[JsonSerializable(typeof(ShoppingSyncResponseJson))]
public sealed partial class ShoppingJson : JsonSerializerContext;
