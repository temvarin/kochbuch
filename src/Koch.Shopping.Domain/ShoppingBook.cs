using Koch.Kernel;
using Koch.Kernel.Measures;

namespace Koch.Shopping.Domain;

/// <summary>
/// One contribution to the household's shopping list: what one recipe needs of one ingredient, or one thing added by
/// hand. Contributions of the same ingredient are shown as one <see cref="ShoppingLine"/>, but stored apart: when both
/// phones add recipes while offline, their contributions simply add up, and neither overwrites the other
/// (docs/adr/0005). There is one list (docs/adr/0014).
/// </summary>
public sealed record ShoppingItem : IItem
{
    public const int MaxName = 80, MaxRecipe = 120;

    /// <param name="amount">null: no amount ("nach Geschmack", or added by hand without one).</param>
    /// <param name="recipe">The recipe it is for; null if added by hand.</param>
    /// <exception cref="ArgumentException">No id or name, or texts too long.</exception>
    public ShoppingItem(Guid id, string name, Quantity? amount, bool isChecked, string? recipe)
    {
        if (id == Guid.Empty) throw new ArgumentException("An item needs an id.");
        (Id, Amount, Checked) = (id, amount, isChecked);
        Name = Text.Check(Text.OneLine(name), MaxName, "name");
        Recipe = recipe is null ? null : Text.Check(Text.OneLine(recipe), MaxRecipe, "recipe");
    }

    public Guid Id { get; }

    public string Name { get; }

    public Quantity? Amount { get; }

    /// <summary>In the trolley.</summary>
    public bool Checked { get; }

    public string? Recipe { get; }

    /// <summary>Contributions with the same key are one line: case and spacing don't count.</summary>
    public string Key => Text.OneLine(Name).ToLowerInvariant();

    public ShoppingItem WithChecked(bool isChecked) => new(Id, Name, Amount, isChecked, Recipe);
}

/// <summary>What the app hands over to put on the list: one ingredient line of a recipe, already scaled to the servings.</summary>
public sealed record PlannedLine(string Ingredient, Quantity? Amount, string? Recipe);

/// <summary>
/// One line of the shopping list as the phone shows it: every contribution of an ingredient together.
/// </summary>
/// <param name="Missing">What is still to buy: the unticked contributions, added up. Empty when all is ticked, or none had an amount.</param>
/// <param name="Total">Everything, ticked or not, added up.</param>
/// <param name="Checked">All contributions are ticked. A new one (another recipe added) unticks the line again.</param>
/// <param name="Recipes">Which recipes it is for.</param>
public sealed record ShoppingLine(string Key, string Name, IReadOnlyList<Quantity> Missing,
    IReadOnlyList<Quantity> Total, bool Checked, IReadOnlyList<string> Recipes);

/// <summary>The household's shopping list, shared through the Shopping service. Immutable.</summary>
public sealed record ShoppingBook : ISyncBook<ShoppingBook, ShoppingChanges>
{
    public static ShoppingBook Empty { get; } = new([]);

    /// <exception cref="ArgumentException">Two items with the same id.</exception>
    public ShoppingBook(IEnumerable<ShoppingItem> items)
    {
        Items = items.ToValueList();
        if (Items.DistinctBy(i => i.Id).Count() != Items.Count) throw new ArgumentException("Two items share an id.");
    }

    public ValueList<ShoppingItem> Items { get; }

    /// <summary>Puts recipe lines on the list, each as its own unticked contribution.</summary>
    /// <param name="newId">A fresh id per item; tests pass a predictable one.</param>
    public ShoppingBook Add(IEnumerable<PlannedLine> lines, Func<Guid> newId) =>
        new([.. Items, .. lines.Select(l => new ShoppingItem(newId(), l.Ingredient, l.Amount, false, l.Recipe))]);

    /// <summary>Ticks or unticks every contribution of the line.</summary>
    public ShoppingBook Check(string key, bool isChecked) =>
        new(Items.Select(i => i.Key == key && i.Checked != isChecked ? i.WithChecked(isChecked) : i));

    /// <summary>Takes everything ticked off the list.</summary>
    public ShoppingBook RemoveChecked() => new(Items.Where(i => !i.Checked));

    /// <summary>The recipes something is still to be bought for, by name.</summary>
    public IReadOnlyList<string> OpenRecipes =>
        [.. Items.Where(i => !i.Checked).Select(i => i.Recipe).OfType<string>().Distinct().Order(StringComparer.Ordinal)];

    /// <summary>The list's lines: what is still to buy first, each part by name (docs/adr/0013: no aisles).</summary>
    public IReadOnlyList<ShoppingLine> Lines() =>
    [
        .. Items.GroupBy(i => i.Key).Select(group =>
        {
            var items = group.ToList();
            // The same name on every phone, and capitalised if anyone wrote it so ("Kartoffeln" sorts before
            // "kartoffeln" by character code).
            var first = items.OrderBy(i => i.Name, StringComparer.Ordinal).ThenBy(i => i.Id).First();
            return new ShoppingLine(group.Key, first.Name,
                Amounts.Combine(items.Where(i => !i.Checked && i.Amount is not null).Select(i => i.Amount!)),
                Amounts.Combine(items.Where(i => i.Amount is not null).Select(i => i.Amount!)),
                items.All(i => i.Checked),
                [.. items.Select(i => i.Recipe).OfType<string>().Distinct().Order(StringComparer.Ordinal)]);
        })
        .OrderBy(l => l.Checked).ThenBy(l => l.Key, StringComparer.Ordinal),
    ];

    public ShoppingBook Apply(ShoppingChanges changes) => new(changes.Items.ApplyTo(Items));
}

/// <summary>What changed in a <see cref="ShoppingBook"/>.</summary>
public sealed record ShoppingChanges(ItemChanges<ShoppingItem> Items) : IBookChanges<ShoppingBook, ShoppingChanges>
{
    public static ShoppingChanges None { get; } = new(ItemChanges.None<ShoppingItem>());

    public bool IsEmpty => Items.IsEmpty;

    public static ShoppingChanges Between(ShoppingBook before, ShoppingBook after) => new(ItemChanges.Between(before.Items, after.Items));

    public ShoppingChanges Union(ShoppingChanges other) => new(Items.Union(other.Items));

    public ShoppingChanges Now(ShoppingBook book) => new(Items.Now(book.Items));
}

/// <summary>The shopping list on a phone and what the phone knows about its sync.</summary>
public sealed class SyncedShopping : Synced<ShoppingBook, ShoppingChanges>
{
    public SyncedShopping()
    {
    }

    public SyncedShopping(ShoppingBook book, SyncState<ShoppingBook>? state, ShoppingChanges? unconfirmed)
        : base(book, state, unconfirmed)
    {
    }
}
