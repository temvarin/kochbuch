using Koch.Recipes.Domain.Catalog;
using Koch.Recipes.Domain.Cooking;
using Koch.Kernel;

namespace Koch.Recipes.Domain.Sync;

/// <summary>
/// Everything the household shares through the Recipes service: its recipes. Immutable; every change gives a new book.
/// </summary>
public sealed record RecipeBook : ISyncBook<RecipeBook, BookChanges>
{
    public static RecipeBook Empty { get; } = new([]);

    /// <exception cref="ArgumentException">Two recipes with the same id.</exception>
    public RecipeBook(IEnumerable<Recipe> recipes)
    {
        Recipes = recipes.ToValueList();
        if (Recipes.DistinctBy(r => r.Id).Count() != Recipes.Count) throw new ArgumentException("Two recipes share an id.");
    }

    public ValueList<Recipe> Recipes { get; }

    public Recipe? Find(Guid id) => Recipes.FirstOrDefault(r => r.Id == id);

    /// <summary>Every ingredient the recipes name, once, by name: the suggestions while typing.</summary>
    public IReadOnlyList<string> IngredientNames =>
    [
        .. Recipes.SelectMany(r => r.Lines).Select(l => l.Ingredient)
            .DistinctBy(IngredientKey.Of).Order(StringComparer.OrdinalIgnoreCase),
    ];

    /// <summary>Adds or replaces the recipe.</summary>
    public RecipeBook Save(Recipe recipe) =>
        new(Find(recipe.Id) is null ? [.. Recipes, recipe] : Recipes.Select(r => r.Id == recipe.Id ? recipe : r));

    public RecipeBook Remove(Guid recipeId) => new(Recipes.Where(r => r.Id != recipeId));

    public RecipeBook Apply(BookChanges changes) => new(changes.Recipes.ApplyTo(Recipes));

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
public sealed record BookChanges(ItemChanges<Recipe> Recipes) : IBookChanges<RecipeBook, BookChanges>
{
    public static BookChanges None { get; } = new(ItemChanges.None<Recipe>());

    public bool IsEmpty => Recipes.IsEmpty;

    public static BookChanges Between(RecipeBook before, RecipeBook after) => new(ItemChanges.Between(before.Recipes, after.Recipes));

    /// <summary>For an item in both, this one's version.</summary>
    public BookChanges Union(BookChanges other) => new(Recipes.Union(other.Recipes));

    /// <summary>The same items as they are in <paramref name="book"/> now.</summary>
    public BookChanges Now(RecipeBook book) => new(Recipes.Now(book.Recipes));

    /// <summary>
    /// What the service needs to merge the changed recipes with edits from the other phone: each one as the phone last
    /// got it from the service (docs/adr/0012); none for a new recipe, none before the first sync.
    /// <para>
    /// Also after syncs that got no answer, which is what editing offline looks like. If such a sync did arrive, the
    /// service already has the phone's earlier edit; the merge keeps it unless the phone changed the same part again.
    /// Only one case comes out wrong: a part changed in the lost sync and then changed back to exactly what the base
    /// had. The service keeps the earlier edit, and the phone shows it after the sync, to be changed back again.
    /// </para>
    /// </summary>
    /// <param name="confirmed">The book as the service last confirmed it (the sync base); null before the first sync.</param>
    public IReadOnlyList<Recipe> BasesIn(RecipeBook? confirmed) => confirmed is null
        ? []
        : [.. Recipes.Changed.Select(r => confirmed.Find(r.Id)).OfType<Recipe>()];
}
