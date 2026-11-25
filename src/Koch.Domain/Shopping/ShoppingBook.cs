using Koch.Domain.Kernel;
using Koch.Domain.Measures;

namespace Koch.Domain.Shopping;

public sealed record ShoppingItem : IItem
{
    public const int MaxName = 80, MaxRecipe = 120;

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

    public bool Checked { get; }

    public string? Recipe { get; }

    public string Key => Text.OneLine(Name).ToLowerInvariant();

    public ShoppingItem WithChecked(bool isChecked) => new(Id, Name, Amount, isChecked, Recipe);
}

public sealed record PlannedLine(string Ingredient, Quantity? Amount, string? Recipe);

public sealed record ShoppingLine(string Key, string Name, IReadOnlyList<Quantity> Missing,
    IReadOnlyList<Quantity> Total, bool Checked, IReadOnlyList<string> Recipes);

public sealed record ShoppingBook : ISyncBook<ShoppingBook, ShoppingChanges>
{
    public static ShoppingBook Empty { get; } = new([]);

    public ShoppingBook(IEnumerable<ShoppingItem> items)
    {
        Items = items.ToValueList();
        if (Items.DistinctBy(i => i.Id).Count() != Items.Count) throw new ArgumentException("Two items share an id.");
    }

    public ValueList<ShoppingItem> Items { get; }

    public ShoppingBook Add(IEnumerable<PlannedLine> lines, Func<Guid> newId) =>
        new([.. Items, .. lines.Select(l => new ShoppingItem(newId(), l.Ingredient, l.Amount, false, l.Recipe))]);

    public ShoppingBook Check(string key, bool isChecked) =>
        new(Items.Select(i => i.Key == key && i.Checked != isChecked ? i.WithChecked(isChecked) : i));

    public ShoppingBook RemoveChecked() => new(Items.Where(i => !i.Checked));

    public IReadOnlyList<string> OpenRecipes =>
        [.. Items.Where(i => !i.Checked).Select(i => i.Recipe).OfType<string>().Distinct().Order(StringComparer.Ordinal)];

    public IReadOnlyList<ShoppingLine> Lines() =>
    [
        .. Items.GroupBy(i => i.Key).Select(group =>
        {
            var items = group.ToList();
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

public sealed record ShoppingChanges(ItemChanges<ShoppingItem> Items) : IBookChanges<ShoppingBook, ShoppingChanges>
{
    public static ShoppingChanges None { get; } = new(ItemChanges.None<ShoppingItem>());

    public bool IsEmpty => Items.IsEmpty;

    public static ShoppingChanges Between(ShoppingBook before, ShoppingBook after) => new(ItemChanges.Between(before.Items, after.Items));

    public ShoppingChanges Union(ShoppingChanges other) => new(Items.Union(other.Items));

    public ShoppingChanges Now(ShoppingBook book) => new(Items.Now(book.Items));
}

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
