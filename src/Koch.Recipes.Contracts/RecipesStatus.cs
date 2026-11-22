using System.Text.Json;
using System.Text.Json.Serialization;

namespace Koch.Recipes.Contracts;

/// <summary>The answer of <c>/api/recipes/status</c>: the whole path from the phone to the database works.</summary>
/// <param name="User">The user id the service received from the gateway.</param>
/// <param name="Database">Whether the service could read its tables with its own identity.</param>
public sealed record RecipesStatus(string User, bool Database);

/// <summary>
/// The serializer for the Recipes API, generated at build time (no reflection in the browser). Missing or null
/// required values are refused; unknown properties are ignored, so an older app can read a newer service's answer
/// that gained a field (the protocol number guards everything else).
/// </summary>
[JsonSourceGenerationOptions(JsonSerializerDefaults.Web,
    RespectNullableAnnotations = true, RespectRequiredConstructorParameters = true)]
[JsonSerializable(typeof(RecipesStatus))]
[JsonSerializable(typeof(SyncRequestJson))]
[JsonSerializable(typeof(SyncResponseJson))]
public sealed partial class RecipesJson : JsonSerializerContext;
