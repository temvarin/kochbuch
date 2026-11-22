using Koch.App.Shell;
using Koch.Kernel;
using Koch.Recipes.Domain.Catalog;
using Koch.Recipes.Domain.Cooking;

namespace Koch.App.Recipes;

/// <summary>What the recipe pages may read and do. Every change is saved on the phone at once and synced soon after.</summary>
public sealed class RecipeService(RecipeState state)
{
    private Koch.Recipes.Domain.Sync.RecipeBook Book => state.Data.Book;

    /// <summary>By title. The UI is German-only without culture data, so case is ignored, nothing more.</summary>
    public IReadOnlyList<Recipe> All => [.. Book.Recipes.OrderBy(r => r.Title, StringComparer.OrdinalIgnoreCase)];

    public Recipe? Find(Guid id) => Book.Find(id);

    /// <summary>
    /// The catalogue by name, for suggestions while typing and for the aisles, plus every ingredient a recipe names that
    /// the catalogue lacks (in <see cref="Aisle.Sonstiges"/> until set): a recipe synced from an older or another client
    /// may not have brought its catalogue entries.
    /// </summary>
    public IReadOnlyList<Ingredient> Ingredients =>
    [
        .. Book.Ingredients
            .Concat(Book.Recipes.SelectMany(r => r.Lines).Select(l => new Ingredient(l.Ingredient, Aisle.Sonstiges)))
            .DistinctBy(i => i.Id)
            .OrderBy(i => i.Name, StringComparer.OrdinalIgnoreCase),
    ];

    public IReadOnlyList<string> IngredientNames => [.. Ingredients.Select(i => i.Name)];

    /// <summary>Where the ingredient is in the shop, if the catalogue knows it.</summary>
    public Aisle? AisleOf(string ingredient) =>
        Book.Ingredients.FirstOrDefault(i => i.Id == IngredientKey.Of(ingredient))?.Aisle;

    public void Save(Recipe recipe) => state.Change(data => data.Change(b => b.Save(recipe)));

    public void Remove(Guid id) => state.Change(data => data.Change(b => b.Remove(id)));

    /// <summary>Moves the ingredient to another aisle, on both phones and on every shopping list that has it.</summary>
    public void SetAisle(string ingredient, Aisle aisle) =>
        state.Change(data => data.Change(b => b.Save(new Ingredient(ingredient, aisle))));
}
