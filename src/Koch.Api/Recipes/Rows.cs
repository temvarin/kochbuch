using System.Text.Json;
using System.Text.Json.Serialization;
using Koch.Api.Storage;
using Koch.Contracts;
using Koch.Domain.Recipes;

namespace Koch.Api.Recipes;

[JsonSourceGenerationOptions(JsonSerializerDefaults.Web,
    RespectNullableAnnotations = true, RespectRequiredConstructorParameters = true)]
[JsonSerializable(typeof(RecipeJson))]
public sealed partial class RecipeRowJson : JsonSerializerContext;

public static class Rows
{
    private const string RecipePrefix = "recipe:";

    public static IReadOnlyList<RowWrite> For(BookChanges changes, IReadOnlyList<Recipe>? bases = null)
    {
        var baseOf = (bases ?? []).ToDictionary(r => r.Id);
        return
        [
            .. changes.Recipes.Changed.Select(r => new RowWrite(RecipePrefix + r.Id, Write(r),
                baseOf.TryGetValue(r.Id, out var baseRecipe) ? stored => Write(RecipeMerge.Merge(baseRecipe, ReadRecipe(stored), r)) : null)),
            .. changes.Recipes.Removed.Select(id => new RowWrite(RecipePrefix + id, null)),
        ];
    }

    private static string Write(Recipe recipe) => JsonSerializer.Serialize(recipe.ToJson(), RecipeRowJson.Default.RecipeJson);

    private static Recipe ReadRecipe(string json) =>
        (JsonSerializer.Deserialize(json, RecipeRowJson.Default.RecipeJson) ?? throw new JsonException("A recipe row is null.")).ToDomain();

    public static RecipeBook ToBook(IEnumerable<StoredRow> rows) => new(rows
        .Where(r => r.Id.StartsWith(RecipePrefix, StringComparison.Ordinal))
        .Select(r => (JsonSerializer.Deserialize(r.Json, RecipeRowJson.Default.RecipeJson)
            ?? throw new JsonException($"Row {r.Id} is null.")).ToDomain()));
}
