using Koch.App.Recipes;
using Koch.App.Shell;
using Koch.Kernel;
using Koch.Recipes.Domain.Cooking;
using Koch.Shopping.Domain;

namespace Koch.App.Shopping;

/// <summary>A recipe chosen for a list, with the servings it is to be made for.</summary>
public sealed record Choice(Recipe Recipe, int Servings);

/// <summary>
/// What the shopping pages may read and do. This is where recipes meet lists: the app turns the chosen recipes into
/// planned lines (scaled to the servings, with the catalogue's aisle), and the shopping domain puts them on the list.
/// The Shopping service never sees a recipe (docs/adr/0005).
/// </summary>
public sealed class ShoppingService(ShoppingState state, RecipeService recipes, TimeProvider clock)
{
    private ShoppingBook Book => state.Data.Book;

    /// <summary>Newest first.</summary>
    public IReadOnlyList<ShoppingList> Lists => [.. Book.Lists.OrderByDescending(l => l.Created)];

    public ShoppingList? Find(Guid id) => Book.Find(id);

    /// <summary>In the order of a walk through the shop, by the catalogue's current aisles.</summary>
    public IReadOnlyList<ShoppingLine> Lines(Guid listId) => Book.Lines(listId, recipes.AisleOf);

    /// <returns>The new list's id.</returns>
    public Guid NewList(string? title = null)
    {
        var now = clock.GetUtcNow();
        var list = new ShoppingList(Guid.NewGuid(), title ?? $"Einkauf {TimeZoneInfo.ConvertTime(now, clock.LocalTimeZone):dd.MM.}", now);
        state.Change(data => data.Change(b => b.Save(list)));
        return list.Id;
    }

    /// <summary>Every ingredient line of the chosen recipes, scaled to their servings.</summary>
    public void Add(Guid listId, IEnumerable<Choice> choices)
    {
        var lines = choices.SelectMany(c => c.Recipe.LinesFor(c.Servings).Select(l =>
            new PlannedLine(l.Ingredient, l.Amount, recipes.AisleOf(l.Ingredient) ?? Aisle.Sonstiges, c.Recipe.Title))).ToList();
        state.Change(data => data.Change(b => b.Add(listId, lines, Guid.NewGuid)));
    }

    /// <summary>Something not from a recipe: "Milch".</summary>
    public void AddByHand(Guid listId, string name) => state.Change(data => data.Change(b =>
        b.Add(listId, [new PlannedLine(name, null, recipes.AisleOf(name) ?? Aisle.Sonstiges, null)], Guid.NewGuid)));

    public void Check(Guid listId, string key, bool isChecked) =>
        state.Change(data => data.Change(b => b.Check(listId, key, isChecked)));

    public void RemoveChecked(Guid listId) => state.Change(data => data.Change(b => b.RemoveChecked(listId)));

    public void RemoveList(Guid listId) => state.Change(data => data.Change(b => b.RemoveList(listId)));
}
