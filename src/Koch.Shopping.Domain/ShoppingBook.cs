using Koch.Kernel;
using Koch.Kernel.Measures;

namespace Koch.Shopping.Domain;

/// <summary>A shopping list: "Wocheneinkauf". Its contents are the <see cref="ShoppingItem"/>s that name it.</summary>
public sealed record ShoppingList : IItem
{
    public const int MaxTitle = 60;

    /// <exception cref="ArgumentException">No id or title, or the title is too long.</exception>
    public ShoppingList(Guid id, string title, DateTimeOffset created)
    {
        if (id == Guid.Empty) throw new ArgumentException("A list needs an id.");
        (Id, Title, Created) = (id, Text.Check(Text.OneLine(title), MaxTitle, "title"), created);
    }

    public Guid Id { get; }

    public string Title { get; }

    /// <summary>Lists are shown newest first.</summary>
    public DateTimeOffset Created { get; }
}

/// <summary>
/// One contribution to a list: what one recipe needs of one ingredient, or one thing added by hand. Contributions of
/// the same ingredient are shown as one <see cref="ShoppingLine"/>, but stored apart: when both phones add recipes to the
/// same list while offline, their contributions simply add up, and neither overwrites the other (docs/adr/0005).
/// </summary>
public sealed record ShoppingItem : IItem
{
    public const int MaxName = 80, MaxRecipe = 120;

    /// <param name="amount">null: no amount ("nach Geschmack", or added by hand without one).</param>
    /// <param name="recipe">The recipe it is for; null if added by hand.</param>
    /// <exception cref="ArgumentException">No id, list or name, or texts too long.</exception>
    public ShoppingItem(Guid id, Guid listId, string name, Quantity? amount, Aisle aisle, bool isChecked, string? recipe)
    {
        if (id == Guid.Empty || listId == Guid.Empty) throw new ArgumentException("An item needs an id and a list.");
        if (!Enum.IsDefined(aisle)) throw new ArgumentException($"Unknown aisle {aisle}.");
        (Id, ListId, Amount, Aisle, Checked) = (id, listId, amount, aisle, isChecked);
        Name = Text.Check(Text.OneLine(name), MaxName, "name");
        Recipe = recipe is null ? null : Text.Check(Text.OneLine(recipe), MaxRecipe, "recipe");
    }

    public Guid Id { get; }

    public Guid ListId { get; }

    public string Name { get; }

    public Quantity? Amount { get; }

    public Aisle Aisle { get; }

    /// <summary>In the trolley.</summary>
    public bool Checked { get; }

    public string? Recipe { get; }

    /// <summary>Contributions with the same key are one line: case and spacing don't count.</summary>
    public string Key => Text.OneLine(Name).ToLowerInvariant();

    public ShoppingItem WithChecked(bool isChecked) => new(Id, ListId, Name, Amount, Aisle, isChecked, Recipe);
}

/// <summary>What the app hands over to put on a list: one ingredient line of a recipe, already scaled to the servings.</summary>
public sealed record PlannedLine(string Ingredient, Quantity? Amount, Aisle Aisle, string? Recipe);

/// <summary>
/// One line of a shopping list as the phone shows it: every contribution of an ingredient together.
/// </summary>
/// <param name="Missing">What is still to buy: the unticked contributions, added up. Empty when all is ticked, or none had an amount.</param>
/// <param name="Total">Everything, ticked or not, added up.</param>
/// <param name="Checked">All contributions are ticked. A new one (another recipe added) unticks the line again.</param>
/// <param name="Recipes">Which recipes it is for.</param>
public sealed record ShoppingLine(string Key, string Name, Aisle Aisle, IReadOnlyList<Quantity> Missing,
    IReadOnlyList<Quantity> Total, bool Checked, IReadOnlyList<string> Recipes);

/// <summary>All shopping lists the household shares through the Shopping service. Immutable.</summary>
public sealed record ShoppingBook : ISyncBook<ShoppingBook, ShoppingChanges>
{
    public static ShoppingBook Empty { get; } = new([], []);

    /// <exception cref="ArgumentException">Two lists or two items with the same id.</exception>
    public ShoppingBook(IEnumerable<ShoppingList> lists, IEnumerable<ShoppingItem> items)
    {
        Lists = lists.ToValueList();
        Items = items.ToValueList();
        if (Lists.DistinctBy(l => l.Id).Count() != Lists.Count) throw new ArgumentException("Two lists share an id.");
        if (Items.DistinctBy(i => i.Id).Count() != Items.Count) throw new ArgumentException("Two items share an id.");
    }

    public ValueList<ShoppingList> Lists { get; }

    /// <summary>
    /// May include items of a list that is gone: one phone removed the list while the other added to it. They are kept
    /// (the sync never drops data) but not shown.
    /// </summary>
    public ValueList<ShoppingItem> Items { get; }

    public ShoppingList? Find(Guid listId) => Lists.FirstOrDefault(l => l.Id == listId);

    public ShoppingBook Save(ShoppingList list) => new([.. Lists.Where(l => l.Id != list.Id), list], Items);

    /// <summary>Removes the list and everything on it.</summary>
    public ShoppingBook RemoveList(Guid listId) =>
        new(Lists.Where(l => l.Id != listId), Items.Where(i => i.ListId != listId));

    /// <summary>Puts recipe lines on the list, each as its own unticked contribution.</summary>
    /// <param name="newId">A fresh id per item; tests pass a predictable one.</param>
    public ShoppingBook Add(Guid listId, IEnumerable<PlannedLine> lines, Func<Guid> newId)
    {
        if (Find(listId) is null) throw new ArgumentException("No such list.");
        return new(Lists, [.. Items, .. lines.Select(l => new ShoppingItem(newId(), listId, l.Ingredient, l.Amount, l.Aisle, false, l.Recipe))]);
    }

    /// <summary>Ticks or unticks every contribution of the line.</summary>
    public ShoppingBook Check(Guid listId, string key, bool isChecked) => new(Lists,
        Items.Select(i => i.ListId == listId && i.Key == key && i.Checked != isChecked ? i.WithChecked(isChecked) : i));

    /// <summary>Takes everything ticked off the list.</summary>
    public ShoppingBook RemoveChecked(Guid listId) => new(Lists, Items.Where(i => i.ListId != listId || !i.Checked));

    /// <summary>The list's lines in the order of a walk through the shop, then by name.</summary>
    /// <param name="aisleOf">Where an ingredient is now, if known (the catalogue may have changed since it was added).</param>
    public IReadOnlyList<ShoppingLine> Lines(Guid listId, Func<string, Aisle?>? aisleOf = null) =>
    [
        .. Items.Where(i => i.ListId == listId).GroupBy(i => i.Key).Select(group =>
        {
            var items = group.ToList();
            // The same name and aisle on every phone, and capitalised if anyone wrote it so ("Kartoffeln" sorts before
            // "kartoffeln" by character code).
            var first = items.OrderBy(i => i.Name, StringComparer.Ordinal).ThenBy(i => i.Id).First();
            return new ShoppingLine(group.Key, first.Name, aisleOf?.Invoke(first.Name) ?? first.Aisle,
                Amounts.Combine(items.Where(i => !i.Checked && i.Amount is not null).Select(i => i.Amount!)),
                Amounts.Combine(items.Where(i => i.Amount is not null).Select(i => i.Amount!)),
                items.All(i => i.Checked),
                [.. items.Select(i => i.Recipe).OfType<string>().Distinct().Order(StringComparer.Ordinal)]);
        })
        .OrderBy(l => l.Checked).ThenBy(l => l.Aisle).ThenBy(l => l.Key, StringComparer.Ordinal),
    ];

    public ShoppingBook Apply(ShoppingChanges changes) => new(changes.Lists.ApplyTo(Lists), changes.Items.ApplyTo(Items));
}

/// <summary>What changed in a <see cref="ShoppingBook"/>.</summary>
public sealed record ShoppingChanges(ItemChanges<ShoppingList> Lists, ItemChanges<ShoppingItem> Items)
    : IBookChanges<ShoppingBook, ShoppingChanges>
{
    public static ShoppingChanges None { get; } = new(ItemChanges.None<ShoppingList>(), ItemChanges.None<ShoppingItem>());

    public bool IsEmpty => Lists.IsEmpty && Items.IsEmpty;

    public static ShoppingChanges Between(ShoppingBook before, ShoppingBook after) =>
        new(ItemChanges.Between(before.Lists, after.Lists), ItemChanges.Between(before.Items, after.Items));

    public ShoppingChanges Union(ShoppingChanges other) => new(Lists.Union(other.Lists), Items.Union(other.Items));

    public ShoppingChanges Now(ShoppingBook book) => new(Lists.Now(book.Lists), Items.Now(book.Items));
}

/// <summary>The shopping lists on a phone and what the phone knows about their sync.</summary>
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
