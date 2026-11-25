using Koch.Domain.Kernel;

namespace Koch.Domain.Recipes;

public sealed record RecipeBook : ISyncBook<RecipeBook, BookChanges>
{
    public static RecipeBook Empty { get; } = new([]);

    public RecipeBook(IEnumerable<Recipe> recipes)
    {
        Recipes = recipes.ToValueList();
        if (Recipes.DistinctBy(r => r.Id).Count() != Recipes.Count) throw new ArgumentException("Two recipes share an id.");
    }

    public ValueList<Recipe> Recipes { get; }

    public Recipe? Find(Guid id) => Recipes.FirstOrDefault(r => r.Id == id);

    public IReadOnlyList<string> IngredientNames =>
    [
        .. Recipes.SelectMany(r => r.Lines).Select(l => l.Ingredient)
            .DistinctBy(IngredientKey.Of).Order(StringComparer.OrdinalIgnoreCase),
    ];

    public RecipeBook Save(Recipe recipe) =>
        new(Find(recipe.Id) is null ? [.. Recipes, recipe] : Recipes.Select(r => r.Id == recipe.Id ? recipe : r));

    public RecipeBook Remove(Guid recipeId) => new(Recipes.Where(r => r.Id != recipeId));

    public RecipeBook Apply(BookChanges changes) => new(changes.Recipes.ApplyTo(Recipes));

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

public sealed record BookChanges(ItemChanges<Recipe> Recipes) : IBookChanges<RecipeBook, BookChanges>
{
    public static BookChanges None { get; } = new(ItemChanges.None<Recipe>());

    public bool IsEmpty => Recipes.IsEmpty;

    public static BookChanges Between(RecipeBook before, RecipeBook after) => new(ItemChanges.Between(before.Recipes, after.Recipes));

    public BookChanges Union(BookChanges other) => new(Recipes.Union(other.Recipes));

    public BookChanges Now(RecipeBook book) => new(Recipes.Now(book.Recipes));

    public IReadOnlyList<Recipe> BasesIn(RecipeBook? confirmed) => confirmed is null
        ? []
        : [.. Recipes.Changed.Select(r => confirmed.Find(r.Id)).OfType<Recipe>()];
}

public sealed class SyncedBook : Synced<RecipeBook, BookChanges>
{
    public SyncedBook()
    {
    }

    public SyncedBook(RecipeBook book, SyncState<RecipeBook>? state, BookChanges? unconfirmed) : base(book, state, unconfirmed)
    {
    }
}
