namespace Koch.Shopping.Domain.Tests;

/// <summary>Fixed data for contract samples: the same every run.</summary>
public static class Known
{
    public static readonly Guid ListId = new("11111111-1111-1111-1111-111111111111");
    public static readonly DateTimeOffset Now = new(2026, 11, 22, 18, 30, 0, TimeSpan.Zero);

    public static ShoppingBook Lists()
    {
        var next = 1;
        return ShoppingBook.Empty.Save(new ShoppingList(ListId, "Wocheneinkauf", Now)).Add(ListId,
        [
            new PlannedLine("Kartoffeln", new Quantity(500, Unit.Gramm), Aisle.ObstGemuese, "Kartoffelsuppe"),
            new PlannedLine("Salz", null, Aisle.Gewuerze, "Kartoffelsuppe"),
            new PlannedLine("Milch", null, Aisle.Kuehlregal, null),
        ], () => new Guid($"22222222-2222-2222-2222-{next++:D12}"));
    }
}
