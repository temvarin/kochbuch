using Koch.App.Shell;
using Koch.Domain.Recipes;

namespace Koch.App.Recipes;

/// <summary>What the recipe pages may read and do. Every change is saved on the phone at once and synced soon after.</summary>
public sealed class RecipeService(RecipeState state)
{
    private RecipeBook Book => state.Data.Book;

    /// <summary>By title. The UI is German-only without culture data, so case is ignored, nothing more.</summary>
    public IReadOnlyList<Recipe> All => [.. Book.Recipes.OrderBy(r => r.Title, StringComparer.OrdinalIgnoreCase)];

    public Recipe? Find(Guid id) => Book.Find(id);

    /// <summary>Every ingredient the recipes name, for suggestions while typing.</summary>
    public IReadOnlyList<string> IngredientNames => Book.IngredientNames;

    public void Save(Recipe recipe) => state.Change(data => data.Change(b => b.Save(recipe)));

    public void Remove(Guid id) => state.Change(data => data.Change(b => b.Remove(id)));
}
