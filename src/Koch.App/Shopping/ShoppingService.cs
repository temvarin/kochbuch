using Koch.App.Shell;
using Koch.Domain.Recipes;
using Koch.Domain.Shopping;

namespace Koch.App.Shopping;

/// <summary>
/// What the shopping pages may read and do. This is where recipes meet the list: the app turns a recipe into planned
/// lines scaled to the servings, and the shopping domain puts them on the one list (docs/adr/0005, 0014). The Shopping
/// service never sees a recipe.
/// </summary>
public sealed class ShoppingService(ShoppingState state)
{
    private ShoppingBook Book => state.Data.Book;

    /// <summary>What is still to buy first, by name.</summary>
    public IReadOnlyList<ShoppingLine> Lines => Book.Lines();

    /// <summary>How many lines are still to buy.</summary>
    public int Open => Lines.Count(l => !l.Checked);

    /// <summary>The recipes something is still to be bought for.</summary>
    public IReadOnlyList<string> OpenRecipes => Book.OpenRecipes;

    /// <summary>Every ingredient line of the recipe, scaled to <paramref name="servings"/>.</summary>
    public void Add(Recipe recipe, int servings) => Change(b => b.Add(
        recipe.LinesFor(servings).Select(l => new PlannedLine(l.Ingredient, l.Amount, recipe.Title)), Guid.NewGuid));

    /// <summary>Something not from a recipe: "Milch".</summary>
    public void AddByHand(string name) => Change(b => b.Add([new PlannedLine(name, null, null)], Guid.NewGuid));

    public void Check(string key, bool isChecked) => Change(b => b.Check(key, isChecked));

    public void RemoveChecked() => Change(b => b.RemoveChecked());

    public void Clear() => Change(_ => ShoppingBook.Empty);

    private void Change(Func<ShoppingBook, ShoppingBook> change) => state.Change(data => data.Change(change));
}
