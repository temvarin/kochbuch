namespace Koch.Shopping.Domain.Tests;

/// <summary>Fixed data for contract samples: the same every run.</summary>
public static class Known
{
    public static readonly DateTimeOffset Now = new(2026, 11, 22, 18, 30, 0, TimeSpan.Zero);

    public static ShoppingBook List()
    {
        var next = 1;
        return ShoppingBook.Empty.Add(
        [
            new PlannedLine("Kartoffeln", new Quantity(500, Unit.Gramm), "Kartoffelsuppe"),
            new PlannedLine("Salz", null, "Kartoffelsuppe"),
            new PlannedLine("Milch", null, null),
        ], () => new Guid($"22222222-2222-2222-2222-{next++:D12}"));
    }
}
