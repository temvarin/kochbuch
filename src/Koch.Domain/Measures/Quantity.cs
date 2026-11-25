namespace Koch.Domain.Measures;

/// <summary>An amount of something: 500 g, 2 EL, 1 Dose. Always more than zero; "nach Geschmack" is no quantity.</summary>
public sealed record Quantity
{
    public const decimal Max = 1_000_000;

    public Quantity(decimal amount, Unit unit)
    {
        if (amount <= 0 || amount > Max) throw new ArgumentException($"A quantity must be above 0 and at most {Max}.");
        if (!Enum.IsDefined(unit)) throw new ArgumentException($"Unknown unit {unit}.");
        (Amount, Unit) = (amount, unit);
    }

    public decimal Amount { get; }

    public Unit Unit { get; }

    public Quantity Scale(decimal factor) => new(Math.Clamp(Math.Round(Amount * factor, 3), 0.001m, Max), Unit);

    /// <summary>
    /// Grams and millilitres as kilograms and litres from 1000 on, and the other way round below 1.
    /// Spoons and counted units stay as they are.
    /// </summary>
    public Quantity Readable() => Unit switch
    {
        Unit.Gramm or Unit.Kilogramm => InLargerFrom1000(Unit.Gramm, Unit.Kilogramm),
        Unit.Milliliter or Unit.Liter => InLargerFrom1000(Unit.Milliliter, Unit.Liter),
        _ => this,
    };

    private Quantity InLargerFrom1000(Unit small, Unit large)
    {
        var inSmall = Amount * Unit.InBase()!.Value;
        return inSmall >= 1000 ? new(inSmall / 1000, large) : new(inSmall, small);
    }
}
