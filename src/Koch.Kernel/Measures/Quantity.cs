namespace Koch.Kernel.Measures;

/// <summary>An amount of something: 500 g, 2 EL, 1 Dose. Always more than zero; "nach Geschmack" is no quantity.</summary>
public sealed record Quantity
{
    /// <summary>Far above any recipe, low enough that scaling stays exact in decimal.</summary>
    public const decimal Max = 1_000_000;

    /// <exception cref="ArgumentException">Not above zero, or absurdly large.</exception>
    public Quantity(decimal amount, Unit unit)
    {
        if (amount <= 0 || amount > Max) throw new ArgumentException($"A quantity must be above 0 and at most {Max}.");
        if (!Enum.IsDefined(unit)) throw new ArgumentException($"Unknown unit {unit}.");
        (Amount, Unit) = (amount, unit);
    }

    public decimal Amount { get; }

    public Unit Unit { get; }

    /// <summary>For another number of servings. Never below a thousandth, so a tiny result stays a quantity.</summary>
    public Quantity Scale(decimal factor) => new(Math.Clamp(Math.Round(Amount * factor, 3), 0.001m, Max), Unit);

    /// <summary>
    /// Grams and millilitres as kilograms and litres from 1000 on, and the other way round below 1: 1500 g reads 1,5 kg,
    /// 0,25 l reads 250 ml. Spoons and counted units stay as they are; 3 TL are not turned into 1 EL.
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
