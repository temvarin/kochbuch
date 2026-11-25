namespace Koch.Domain.Tests;

public class ShoppingBookTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 22, 18, 0, 0, TimeSpan.Zero);

    private static Func<Guid> Ids(int start = 1)
    {
        var next = start;
        return () => new Guid($"00000000-0000-0000-0000-{next++:D12}");
    }

    private static PlannedLine Line(string name, decimal? amount, Unit unit = Unit.Gramm, string recipe = "Suppe") =>
        new(name, amount is { } a ? new Quantity(a, unit) : null, recipe);

    [Fact]
    public void Two_recipes_with_the_same_ingredient_make_one_line_with_the_sum()
    {
        var book = ShoppingBook.Empty
            .Add([Line("Kartoffeln", 500, recipe: "Suppe"), Line("Salz", null)], Ids(1))
            .Add([Line(" kartoffeln", 300, recipe: "Auflauf")], Ids(10));

        var potatoes = Assert.Single(book.Lines(), l => l.Key == "kartoffeln");
        Assert.Equal([new Quantity(800, Unit.Gramm)], potatoes.Missing);
        Assert.Equal(["Auflauf", "Suppe"], potatoes.Recipes.Order(StringComparer.Ordinal));
        Assert.Empty(Assert.Single(book.Lines(), l => l.Key == "salz").Missing);
    }

    [Fact]
    public void Open_lines_come_first_by_name_and_ticked_ones_go_last()
    {
        var book = ShoppingBook.Empty.Add(
            [Line("Milch", 1, Unit.Liter), Line("Brot", 1, Unit.Stueck), Line("Mehl", 1, Unit.Kilogramm)], Ids());
        book = book.Check("brot", true);

        Assert.Equal(["mehl", "milch", "brot"], book.Lines().Select(l => l.Key));
    }

    [Fact]
    public void A_ticked_line_comes_back_with_what_a_newly_added_recipe_still_needs()
    {
        var book = ShoppingBook.Empty.Add([Line("Kartoffeln", 800)], Ids(1)).Check("kartoffeln", true);

        book = book.Add([Line("Kartoffeln", 300, recipe: "Auflauf")], Ids(10));

        var line = Assert.Single(book.Lines());
        Assert.False(line.Checked);
        Assert.Equal([new Quantity(300, Unit.Gramm)], line.Missing);
        Assert.Equal([new Quantity(1.1m, Unit.Kilogramm)], line.Total);
    }

    [Fact]
    public void Removing_the_ticked_items_leaves_the_rest()
    {
        var book = ShoppingBook.Empty.Add([Line("Kartoffeln", 800), Line("Salz", null)], Ids()).Check("salz", true);

        book = book.RemoveChecked();

        Assert.Equal(["kartoffeln"], book.Lines().Select(l => l.Key));
    }

    [Fact]
    public void The_recipes_still_to_shop_for_are_those_with_something_unticked()
    {
        var book = ShoppingBook.Empty
            .Add([Line("Kartoffeln", 500, recipe: "Suppe"), Line("Sahne", 200, Unit.Milliliter, recipe: "Gratin")], Ids())
            .Check("sahne", true);

        Assert.Equal(["Suppe"], book.OpenRecipes);
    }

    private sealed class Service
    {
        public ShoppingBook Book { get; private set; } = ShoppingBook.Empty;

        public void Sync(SyncedShopping phone, bool loseAnswer = false)
        {
            var pending = phone.StartSync();
            Book = Book.Apply(pending.Changes);
            if (loseAnswer) phone.SyncFailed(pending);
            else phone.CompleteSync(pending, Book, Now);
        }
    }

    [Fact]
    public void Recipes_both_phones_add_while_offline_add_up_instead_of_overwriting_each_other()
    {
        var (service, anna, ben) = (new Service(), new SyncedShopping(), new SyncedShopping());
        anna.Change(b => b.Add([Line("Kartoffeln", 500, recipe: "Suppe")], Ids(100)));
        ben.Change(b => b.Add([Line("Kartoffeln", 300, recipe: "Auflauf")], Ids(200)));
        service.Sync(anna);
        service.Sync(ben);
        service.Sync(anna);

        foreach (var phone in new[] { anna, ben })
            Assert.Equal([new Quantity(800, Unit.Gramm)], Assert.Single(phone.Book.Lines()).Missing);
    }

    [Fact]
    public void Whatever_happens_in_the_shop_both_phones_end_up_with_the_services_list()
    {
        string[] names = ["Kartoffeln", "Milch", "Salz", "Mehl"];
        for (var seed = 0; seed < 200; seed++)
        {
            var random = new Random(seed);
            var service = new Service();
            SyncedShopping[] phones = [new(), new()];
            var ids = 0;
            for (var step = 0; step < 40; step++)
            {
                var phone = phones[random.Next(phones.Length)];
                var name = names[random.Next(names.Length)];
                switch (random.Next(5))
                {
                    case 0:
                        var id = new Guid($"00000000-0000-0000-{seed:D4}-{++ids:D12}");
                        phone.Change(b => b.Add([Line(name, random.Next(1, 500))], () => id));
                        break;
                    case 1:
                        phone.Change(b => b.Check(name.ToLowerInvariant(), random.Next(2) == 0));
                        break;
                    case 2:
                        phone.Change(b => b.RemoveChecked());
                        break;
                    case 3:
                        service.Sync(phone, loseAnswer: random.Next(3) == 0);
                        break;
                    case 4:
                        phone.SyncFailed(phone.StartSync());
                        break;
                }
            }

            foreach (var phone in phones) service.Sync(phone);
            foreach (var phone in phones) service.Sync(phone);

            foreach (var phone in phones)
            {
                Assert.Equal(service.Book.Lines(), phone.Book.Lines(), LinesEqual);
                Assert.False(phone.HasUnsyncedChanges, $"seed {seed}");
            }
        }
    }

    private static readonly IEqualityComparer<ShoppingLine> LinesEqual = EqualityComparer<ShoppingLine>.Create(
        (a, b) => a!.Key == b!.Key && a.Checked == b.Checked && a.Missing.SequenceEqual(b.Missing) && a.Total.SequenceEqual(b.Total),
        l => l.Key.GetHashCode(StringComparison.Ordinal));
}
