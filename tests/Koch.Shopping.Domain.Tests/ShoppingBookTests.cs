namespace Koch.Shopping.Domain.Tests;

public class ShoppingBookTests
{
    private static readonly Guid ListId = new("11111111-1111-1111-1111-111111111111");
    private static readonly DateTimeOffset Now = new(2026, 11, 22, 18, 0, 0, TimeSpan.Zero);

    private static ShoppingBook WithList() => ShoppingBook.Empty.Save(new ShoppingList(ListId, "Wocheneinkauf", Now));

    private static Func<Guid> Ids(int start = 1)
    {
        var next = start;
        return () => new Guid($"00000000-0000-0000-0000-{next++:D12}");
    }

    private static PlannedLine Line(string name, decimal? amount, Unit unit = Unit.Gramm, string recipe = "Suppe", Aisle aisle = Aisle.ObstGemuese) =>
        new(name, amount is { } a ? new Quantity(a, unit) : null, aisle, recipe);

    [Fact]
    public void Two_recipes_with_the_same_ingredient_make_one_line_with_the_sum()
    {
        var book = WithList()
            .Add(ListId, [Line("Kartoffeln", 500, recipe: "Suppe"), Line("Salz", null)], Ids(1))
            .Add(ListId, [Line(" kartoffeln", 300, recipe: "Auflauf")], Ids(10));

        var potatoes = Assert.Single(book.Lines(ListId), l => l.Key == "kartoffeln");
        Assert.Equal([new Quantity(800, Unit.Gramm)], potatoes.Missing);
        Assert.Equal(["Auflauf", "Suppe"], potatoes.Recipes.Order(StringComparer.Ordinal));
        Assert.Empty(Assert.Single(book.Lines(ListId), l => l.Key == "salz").Missing);
    }

    [Fact]
    public void Lines_follow_the_walk_through_the_shop_and_ticked_ones_go_last()
    {
        var book = WithList().Add(ListId,
            [Line("Milch", 1, Unit.Liter, aisle: Aisle.Kuehlregal), Line("Äpfel", 1, Unit.Kilogramm), Line("Mehl", 1, Unit.Kilogramm, aisle: Aisle.Backen)], Ids());
        book = book.Check(ListId, "äpfel", true);

        Assert.Equal(["milch", "mehl", "äpfel"], book.Lines(ListId).Select(l => l.Key));
    }

    [Fact]
    public void The_catalogues_current_aisle_wins_over_the_one_the_item_was_added_with()
    {
        var book = WithList().Add(ListId, [Line("Milch", 1, Unit.Liter, aisle: Aisle.Sonstiges), Line("Brot", 1, Unit.Stueck, aisle: Aisle.Brot)], Ids());

        var lines = book.Lines(ListId, name => name == "Milch" ? Aisle.ObstGemuese : null);

        Assert.Equal(["milch", "brot"], lines.Select(l => l.Key));
    }

    /// <summary>Anna has bought the potatoes; meanwhile Ben adds a recipe that needs more. The line comes back with what is missing.</summary>
    [Fact]
    public void A_ticked_line_comes_back_with_what_a_newly_added_recipe_still_needs()
    {
        var book = WithList().Add(ListId, [Line("Kartoffeln", 800)], Ids(1)).Check(ListId, "kartoffeln", true);

        book = book.Add(ListId, [Line("Kartoffeln", 300, recipe: "Auflauf")], Ids(10));

        var line = Assert.Single(book.Lines(ListId));
        Assert.False(line.Checked);
        Assert.Equal([new Quantity(300, Unit.Gramm)], line.Missing);
        Assert.Equal([new Quantity(1.1m, Unit.Kilogramm)], line.Total);
    }

    [Fact]
    public void Removing_the_ticked_items_leaves_the_rest_and_removing_the_list_leaves_nothing()
    {
        var book = WithList().Add(ListId, [Line("Kartoffeln", 800), Line("Salz", null)], Ids()).Check(ListId, "salz", true);

        book = book.RemoveChecked(ListId);
        Assert.Equal(["kartoffeln"], book.Lines(ListId).Select(l => l.Key));

        book = book.RemoveList(ListId);
        Assert.Empty(book.Lists);
        Assert.Empty(book.Items);
    }

    [Fact]
    public void Nothing_goes_on_a_list_that_does_not_exist()
    {
        Assert.Throws<ArgumentException>(() => ShoppingBook.Empty.Add(ListId, [Line("Salz", null)], Ids()));
    }

    // Sync

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
    public void Recipes_both_phones_add_to_one_list_while_offline_add_up_instead_of_overwriting_each_other()
    {
        var (service, anna, ben) = (new Service(), new SyncedShopping(), new SyncedShopping());
        anna.Change(b => b.Save(new ShoppingList(ListId, "Wocheneinkauf", Now)));
        service.Sync(anna);
        service.Sync(ben);

        anna.Change(b => b.Add(ListId, [Line("Kartoffeln", 500, recipe: "Suppe")], Ids(100)));
        ben.Change(b => b.Add(ListId, [Line("Kartoffeln", 300, recipe: "Auflauf")], Ids(200)));
        service.Sync(anna);
        service.Sync(ben);
        service.Sync(anna);

        foreach (var phone in new[] { anna, ben })
            Assert.Equal([new Quantity(800, Unit.Gramm)], Assert.Single(phone.Book.Lines(ListId)).Missing);
    }

    /// <summary>Two phones in the shop tick, untick, add and lose answers in random order; they end up the same.</summary>
    [Fact]
    public void Whatever_happens_in_the_shop_both_phones_end_up_with_the_services_lists()
    {
        string[] names = ["Kartoffeln", "Milch", "Salz", "Mehl"];
        for (var seed = 0; seed < 200; seed++)
        {
            var random = new Random(seed);
            var service = new Service();
            SyncedShopping[] phones = [new(), new()];
            phones[0].Change(b => b.Save(new ShoppingList(ListId, "Einkauf", Now)));
            service.Sync(phones[0]);
            service.Sync(phones[1]);
            var ids = 0;
            for (var step = 0; step < 40; step++)
            {
                var phone = phones[random.Next(phones.Length)];
                var name = names[random.Next(names.Length)];
                switch (random.Next(5))
                {
                    case 0:
                        var id = new Guid($"00000000-0000-0000-{seed:D4}-{++ids:D12}");
                        phone.Change(b => b.Add(ListId, [Line(name, random.Next(1, 500))], () => id));
                        break;
                    case 1:
                        phone.Change(b => b.Check(ListId, name.ToLowerInvariant(), random.Next(2) == 0));
                        break;
                    case 2:
                        phone.Change(b => b.RemoveChecked(ListId));
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
                Assert.Equal(service.Book.Lines(ListId), phone.Book.Lines(ListId), LinesEqual);
                Assert.False(phone.HasUnsyncedChanges, $"seed {seed}");
            }
        }
    }

    private static readonly IEqualityComparer<ShoppingLine> LinesEqual = EqualityComparer<ShoppingLine>.Create(
        (a, b) => a!.Key == b!.Key && a.Checked == b.Checked && a.Missing.SequenceEqual(b.Missing) && a.Total.SequenceEqual(b.Total),
        l => l.Key.GetHashCode(StringComparison.Ordinal));
}
