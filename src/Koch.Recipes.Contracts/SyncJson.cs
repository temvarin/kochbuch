using System.Text.Json.Serialization;

namespace Koch.Recipes.Contracts;

// The shapes of POST /api/recipes/sync. Versioned by SyncRequestJson.Protocol; samples of every released version are
// in tests/Koch.Recipes.Domain.Tests/Contracts, and the rules for changing them in its README.

/// <param name="Unit">The unit as written in a recipe: "g", "kg", "ml", "l", "TL", "EL", "Stück", "Dose", …</param>
public sealed record QuantityJson(decimal Value, string Unit);

/// <param name="Amount">null: no amount ("nach Geschmack").</param>
public sealed record LineJson(string Ingredient, QuantityJson? Amount, string Note);

/// <param name="Photo">Since protocol 2; null or missing: no photo. Missing in rows written before.</param>
public sealed record RecipeJson(
    Guid Id, string Title, int Servings, IReadOnlyList<LineJson> Lines, IReadOnlyList<string> Steps, string Notes,
    Guid? Photo = null);

/// <param name="Aisle">"sonstiges", "obst-gemuese", "kuehlregal", …</param>
public sealed record IngredientJson(string Name, string Aisle);

/// <summary>Changed items travel whole; removed ones as their id.</summary>
/// <param name="Bases">
/// Since protocol 3: some changed recipes as the phone last got them from the service, so it can merge them with the
/// other phone's edits (docs/adr/0012). Missing or empty: the changed recipes win whole.
/// </param>
public sealed record RecipeChangesJson(IReadOnlyList<RecipeJson> Changed, IReadOnlyList<Guid> Removed,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<RecipeJson>? Bases = null);

/// <summary>An ingredient's id is derived from its name, so a removed one travels as that id.</summary>
public sealed record IngredientChangesJson(IReadOnlyList<IngredientJson> Changed, IReadOnlyList<Guid> Removed);

/// <summary>What changed on a phone since its last sync.</summary>
public sealed record SyncRequestJson(int Protocol, RecipeChangesJson Recipes, IngredientChangesJson Ingredients)
{
    /// <summary>
    /// The service answers 409 to any other, and the app asks to be reloaded. 2 since 2026-11-22: recipes have a photo,
    /// which an app of protocol 1 would clear by sending the recipe without it. 3 since 2026-11-22: changed recipes
    /// come with their bases and are merged, so every phone should send them.
    /// </summary>
    public const int CurrentProtocol = 3;
}

/// <summary>The whole book as the service has it after applying the request: the phone's new base.</summary>
public sealed record SyncResponseJson(IReadOnlyList<RecipeJson> Recipes, IReadOnlyList<IngredientJson> Ingredients);
