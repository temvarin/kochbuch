using System.Text.Json;
using System.Text.Json.Serialization;

namespace Koch.Shopping.Contracts;

// The shapes of POST /api/shopping/sync. Versioned by ShoppingSyncRequestJson.Protocol; samples of every released
// version are in tests/Koch.Shopping.Domain.Tests/Contracts.

/// <param name="Unit">"g", "kg", "ml", "l", "TL", "EL", "Stück", "Dose", …</param>
public sealed record AmountJson(decimal Value, string Unit);

/// <param name="Amount">null: no amount.</param>
/// <param name="Recipe">The recipe it is for; null if added by hand.</param>
/// <remarks>
/// Protocol 1 also had "aisle" (docs/adr/0013), protocols 1 and 2 "listId" (docs/adr/0014); readers ignore them in
/// older rows and samples.
/// </remarks>
public sealed record ItemJson(Guid Id, string Name, AmountJson? Amount, bool Checked, string? Recipe);

public sealed record ItemChangesJson(IReadOnlyList<ItemJson> Changed, IReadOnlyList<Guid> Removed);

/// <remarks>Protocols 1 and 2 also had "lists"; readers ignore it (docs/adr/0014).</remarks>
public sealed record ShoppingSyncRequestJson(int Protocol, ItemChangesJson Items)
{
    /// <summary>
    /// The service answers 409 to any other, and the app asks to be reloaded. 2 since 2026-11-22: items have no aisle
    /// (docs/adr/0013). 3 since 2026-11-22: one list (docs/adr/0014).
    /// </summary>
    public const int CurrentProtocol = 3;
}

/// <summary>The whole list as the service has it after applying the request: the phone's new base.</summary>
public sealed record ShoppingSyncResponseJson(IReadOnlyList<ItemJson> Items);

/// <summary>The serializer for the Shopping API, generated at build time (no reflection in the browser).</summary>
[JsonSourceGenerationOptions(JsonSerializerDefaults.Web,
    RespectNullableAnnotations = true, RespectRequiredConstructorParameters = true)]
[JsonSerializable(typeof(ShoppingSyncRequestJson))]
[JsonSerializable(typeof(ShoppingSyncResponseJson))]
public sealed partial class ShoppingJson : JsonSerializerContext;
