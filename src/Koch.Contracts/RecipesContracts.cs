using System.Text.Json;
using System.Text.Json.Serialization;

namespace Koch.Contracts;

/// <summary>The answer of <c>/api/recipes/status</c>: proves the gateway → API → Blob Storage path works.</summary>
public sealed record RecipesStatus(string User, bool Database);

public sealed record QuantityJson(decimal Value, string Unit);

public sealed record LineJson(string Ingredient, QuantityJson? Amount, string Note);

public sealed record RecipeJson(
    Guid Id, string Title, int Servings, IReadOnlyList<LineJson> Lines, IReadOnlyList<string> Steps, string Notes,
    Guid? Photo = null);

public sealed record RecipeChangesJson(IReadOnlyList<RecipeJson> Changed, IReadOnlyList<Guid> Removed,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<RecipeJson>? Bases = null);

public sealed record SyncRequestJson(int Protocol, RecipeChangesJson Recipes)
{
    public const int CurrentProtocol = 5;
}

public sealed record SyncResponseJson(IReadOnlyList<RecipeJson> Recipes);

[JsonSourceGenerationOptions(JsonSerializerDefaults.Web,
    RespectNullableAnnotations = true, RespectRequiredConstructorParameters = true)]
[JsonSerializable(typeof(RecipesStatus))]
[JsonSerializable(typeof(SyncRequestJson))]
[JsonSerializable(typeof(SyncResponseJson))]
public sealed partial class RecipesJson : JsonSerializerContext;
