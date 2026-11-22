using System.Text.Json;
using System.Text.Json.Serialization;
using Koch.Recipes.Contracts;
using Koch.Recipes.Domain.Cooking;
using Koch.Recipes.Domain.Sync;
using Koch.Recipes.Domain.Wire;
using Koch.Recipes.Events;
using Koch.ServiceDefaults.Documents;

namespace Koch.Recipes.Storage;

/// <summary>
/// The rows' JSON, kept apart from the API's serializer: a change to the API must not change stored rows, which are
/// never migrated (tests/Koch.Recipes.Domain.Tests/Contracts, "row-recipe", "row-ingredient").
/// </summary>
[JsonSourceGenerationOptions(JsonSerializerDefaults.Web,
    RespectNullableAnnotations = true, RespectRequiredConstructorParameters = true)]
[JsonSerializable(typeof(RecipeJson))]
[JsonSerializable(typeof(IngredientJson))]
public sealed partial class RowJson : JsonSerializerContext;

/// <summary>Between the recipe book and its rows: one row per recipe and per ingredient.</summary>
public static class Rows
{
    private const string RecipePrefix = "recipe:", IngredientPrefix = "ingredient:";

    /// <param name="bases">
    /// Changed recipes as the phone last got them: those are merged with the stored recipe under its row lock
    /// (<see cref="RecipeMerge"/>, docs/adr/0012). The others replace it whole.
    /// </param>
    public static IReadOnlyList<RowWrite> For(BookChanges changes, IReadOnlyList<Recipe>? bases = null)
    {
        var baseOf = (bases ?? []).ToDictionary(r => r.Id);
        return
        [
            .. changes.Recipes.Changed.Select(r => new RowWrite(RecipePrefix + r.Id, Write(r),
                baseOf.TryGetValue(r.Id, out var baseRecipe) ? stored => Write(RecipeMerge.Merge(baseRecipe, ReadRecipe(stored), r)) : null)),
            .. changes.Recipes.Removed.Select(id => new RowWrite(RecipePrefix + id, null)),
            .. changes.Ingredients.Changed.Select(i => new RowWrite(IngredientPrefix + i.Id, JsonSerializer.Serialize(i.ToJson(), RowJson.Default.IngredientJson))),
            .. changes.Ingredients.Removed.Select(id => new RowWrite(IngredientPrefix + id, null)),
        ];
    }

    private static string Write(Recipe recipe) => JsonSerializer.Serialize(recipe.ToJson(), RowJson.Default.RecipeJson);

    /// <exception cref="JsonException">The row is not what this service writes.</exception>
    private static Recipe ReadRecipe(string json) =>
        (JsonSerializer.Deserialize(json, RowJson.Default.RecipeJson) ?? throw new JsonException("A recipe row is null.")).ToDomain();

    /// <exception cref="JsonException">A row is not what this service writes.</exception>
    public static RecipeBook ToBook(IEnumerable<StoredRow> rows)
    {
        var list = rows.ToList();
        return new RecipeBook(
            list.Where(r => r.Id.StartsWith(RecipePrefix, StringComparison.Ordinal))
                .Select(r => Read(r, RowJson.Default.RecipeJson).ToDomain()),
            list.Where(r => r.Id.StartsWith(IngredientPrefix, StringComparison.Ordinal))
                .Select(r => Read(r, RowJson.Default.IngredientJson).ToDomain()));
    }

    /// <summary>
    /// The photo events a changed row causes: a recipe that now shows another photo, or none, detaches the old one and
    /// attaches the new one. Saved with the row, in the same transaction (docs/adr/0008).
    /// </summary>
    public static IEnumerable<OutboxEvent> PhotoEvents(RowChange change)
    {
        if (!change.Id.StartsWith(RecipePrefix, StringComparison.Ordinal)) yield break;
        var recipe = Guid.Parse(change.Id.AsSpan(RecipePrefix.Length));
        var before = PhotoOf(change.Before);
        var after = PhotoOf(change.After);
        if (before == after) yield break;
        if (before is { } old) yield return Event(Events.PhotoEvents.Detached, old, recipe);
        if (after is { } now) yield return Event(Events.PhotoEvents.Attached, now, recipe);
    }

    private static Guid? PhotoOf(string? json) =>
        json is null ? null : JsonSerializer.Deserialize(json, RowJson.Default.RecipeJson)?.Photo;

    private static OutboxEvent Event(string type, Guid photo, Guid recipe) =>
        new(type, Events.PhotoEvents.Subject(photo), Events.PhotoEvents.Data(new PhotoUsageJson(photo, recipe)));

    private static T Read<T>(StoredRow row, System.Text.Json.Serialization.Metadata.JsonTypeInfo<T> type) =>
        JsonSerializer.Deserialize(row.Json, type) ?? throw new JsonException($"Row {row.Id} is null.");
}
