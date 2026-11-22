using Koch.Recipes.Domain.Catalog;
using Koch.Recipes.Domain.Cooking;
using Koch.Kernel;

namespace Koch.Recipes.Domain.Sync;

/// <summary>
/// Everything the household shares through the Recipes service: the recipes and the ingredient catalogue. Immutable;
/// every change gives a new book.
/// </summary>
public sealed record RecipeBook : ISyncBook<RecipeBook, BookChanges>
{
    public static RecipeBook Empty { get; } = new([], []);

    /// <exception cref="ArgumentException">Two recipes or two ingredients with the same id.</exception>
    public RecipeBook(IEnumerable<Recipe> recipes, IEnumerable<Ingredient> ingredients)
    {
        Recipes = recipes.ToValueList();
        Ingredients = ingredients.ToValueList();
        if (Recipes.DistinctBy(r => r.Id).Count() != Recipes.Count) throw new ArgumentException("Two recipes share an id.");
        if (Ingredients.DistinctBy(i => i.Id).Count() != Ingredients.Count)
            throw new ArgumentException("Two ingredients share a name.");
    }

    public ValueList<Recipe> Recipes { get; }

    public ValueList<Ingredient> Ingredients { get; }

    public Recipe? Find(Guid id) => Recipes.FirstOrDefault(r => r.Id == id);

    /// <summary>
    /// Adds or replaces the recipe, and adds the ingredients it names that the catalogue doesn't know yet (in
    /// <see cref="Aisle.Sonstiges"/>), so they are offered next time.
    /// </summary>
    public RecipeBook Save(Recipe recipe)
    {
        var known = Ingredients.Select(i => i.Id).ToHashSet();
        var added = recipe.Lines.Select(l => l.Ingredient).DistinctBy(IngredientKey.Of)
            .Where(name => !known.Contains(IngredientKey.Of(name))).Select(name => new Ingredient(name, Aisle.Sonstiges));
        var recipes = Find(recipe.Id) is null ? [.. Recipes, recipe] : Recipes.Select(r => r.Id == recipe.Id ? recipe : r);
        return new(recipes, [.. Ingredients, .. added]);
    }

    /// <summary>Removes the recipe; its ingredients stay in the catalogue.</summary>
    public RecipeBook Remove(Guid recipeId) => new(Recipes.Where(r => r.Id != recipeId), Ingredients);

    /// <summary>Adds or replaces the ingredient with the same name (case and spacing don't count).</summary>
    public RecipeBook Save(Ingredient ingredient) =>
        new(Recipes, [.. Ingredients.Where(i => i.Id != ingredient.Id), ingredient]);

    public RecipeBook Apply(BookChanges changes) =>
        new(changes.Recipes.ApplyTo(Recipes), changes.Ingredients.ApplyTo(Ingredients));

    /// <summary>
    /// As the service applies a phone's changes: a changed recipe the phone sent with its base is merged with the one
    /// here (<see cref="RecipeMerge"/>); without a base, or if it is gone here, the phone's version wins whole.
    /// </summary>
    /// <param name="bases">The recipes as the phone last got them, for some of the changed ones.</param>
    public RecipeBook Apply(BookChanges changes, IEnumerable<Recipe> bases)
    {
        var baseOf = bases.ToDictionary(r => r.Id);
        var merged = changes.Recipes.Changed.Select(incoming =>
            baseOf.TryGetValue(incoming.Id, out var baseRecipe) && Find(incoming.Id) is { } current
                ? RecipeMerge.Merge(baseRecipe, current, incoming)
                : incoming);
        return Apply(changes with { Recipes = changes.Recipes with { Changed = [.. merged] } });
    }
}

/// <summary>What changed in a <see cref="RecipeBook"/>.</summary>
public sealed record BookChanges(ItemChanges<Recipe> Recipes, ItemChanges<Ingredient> Ingredients)
    : IBookChanges<RecipeBook, BookChanges>
{
    public static BookChanges None { get; } = new(ItemChanges.None<Recipe>(), ItemChanges.None<Ingredient>());

    public bool IsEmpty => Recipes.IsEmpty && Ingredients.IsEmpty;

    public static BookChanges Between(RecipeBook before, RecipeBook after) =>
        new(ItemChanges.Between(before.Recipes, after.Recipes), ItemChanges.Between(before.Ingredients, after.Ingredients));

    /// <summary>For an item in both, this one's version.</summary>
    public BookChanges Union(BookChanges other) => new(Recipes.Union(other.Recipes), Ingredients.Union(other.Ingredients));

    /// <summary>The same items as they are in <paramref name="book"/> now.</summary>
    public BookChanges Now(RecipeBook book) => new(Recipes.Now(book.Recipes), Ingredients.Now(book.Ingredients));
}
