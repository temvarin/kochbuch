using Koch.Recipes.Contracts;
using Koch.Kernel.Measures;
using Koch.Recipes.Domain.Cooking;
using Koch.Recipes.Domain.Sync;

namespace Koch.Recipes.Domain.Wire;

/// <summary>
/// Between the domain and the JSON shapes in Koch.Recipes.Contracts. Reading goes through the domain's constructors,
/// so JSON that breaks a rule is refused with an <see cref="ArgumentException"/>, whoever sent or stored it.
/// </summary>
public static class Json
{
    public static RecipeJson ToJson(this Recipe recipe) => new(recipe.Id, recipe.Title, recipe.Servings,
        [.. recipe.Lines.Select(l => new LineJson(l.Ingredient, l.Amount?.ToJson(), l.Note))], [.. recipe.Steps], recipe.Notes, recipe.Photo);

    public static Recipe ToDomain(this RecipeJson json) => new(json.Id, json.Title, json.Servings,
        json.Lines.Select(l => new RecipeLine(l.Ingredient, l.Amount?.ToDomain(), l.Note)), json.Steps, json.Notes, json.Photo);

    public static QuantityJson ToJson(this Quantity quantity) => new(quantity.Amount, quantity.Unit.Code());

    public static Quantity ToDomain(this QuantityJson json) => new(json.Value, Units.FromCode(json.Unit));

    /// <param name="bases">The changed recipes as the phone last got them (<see cref="BookChanges.BasesIn"/>).</param>
    public static SyncRequestJson ToJson(this BookChanges changes, IReadOnlyList<Recipe>? bases = null) => new(SyncRequestJson.CurrentProtocol,
        new([.. changes.Recipes.Changed.Select(ToJson)], changes.Recipes.Removed,
            bases is { Count: > 0 } ? [.. bases.Select(ToJson)] : null));

    /// <summary>The bases a phone sent with its changed recipes.</summary>
    /// <exception cref="ArgumentException">A base breaks a rule, appears twice, or belongs to no changed recipe.</exception>
    public static IReadOnlyList<Recipe> BasesToDomain(this SyncRequestJson json)
    {
        List<Recipe> bases = [.. (json.Recipes.Bases ?? []).Select(ToDomain)];
        var changed = json.Recipes.Changed.Select(r => r.Id).ToHashSet();
        if (bases.DistinctBy(r => r.Id).Count() != bases.Count || !bases.All(r => changed.Contains(r.Id)))
            throw new ArgumentException("A base belongs to no changed recipe, or appears twice.");
        return bases;
    }

    /// <exception cref="ArgumentException">An item breaks a rule, or appears twice.</exception>
    public static BookChanges ToDomain(this SyncRequestJson json)
    {
        var changes = new BookChanges(new([.. json.Recipes.Changed.Select(ToDomain)], json.Recipes.Removed));
        if (changes.Recipes.Changed.DistinctBy(r => r.Id).Count() != changes.Recipes.Changed.Count)
            throw new ArgumentException("A recipe appears twice among the changes.");
        return changes;
    }

    public static SyncResponseJson ToJson(this RecipeBook book) => new([.. book.Recipes.Select(ToJson)]);

    public static RecipeBook ToDomain(this SyncResponseJson json) => new(json.Recipes.Select(ToDomain));
}
