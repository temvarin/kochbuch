namespace Koch.Kernel.Measures;

/// <summary>The units a recipe line can use. The wire and storage use <see cref="Units.Code"/>, never the number.</summary>
public enum Unit
{
    Gramm,
    Kilogramm,
    Milliliter,
    Liter,
    Teeloeffel,
    Essloeffel,
    Stueck,
    Dose,
    Bund,
    Zehe,
    Prise,
    Packung,
    Scheibe,
}

/// <summary>What can be converted into what: mass into mass, volume into volume; counted things not at all.</summary>
public enum Dimension
{
    Mass,
    Volume,
    /// <summary>Pieces, cans, bunches …: only the same unit adds up.</summary>
    Count,
}

public static class Units
{
    private sealed record Info(string Code, Dimension Dimension, decimal? InBase);

    // Mass in grams, volume in millilitres. A Teelöffel is 5 ml and an Esslöffel 15 ml, as German recipes count them.
    private static readonly Dictionary<Unit, Info> All = new()
    {
        [Unit.Gramm] = new("g", Dimension.Mass, 1),
        [Unit.Kilogramm] = new("kg", Dimension.Mass, 1000),
        [Unit.Milliliter] = new("ml", Dimension.Volume, 1),
        [Unit.Liter] = new("l", Dimension.Volume, 1000),
        [Unit.Teeloeffel] = new("TL", Dimension.Volume, 5),
        [Unit.Essloeffel] = new("EL", Dimension.Volume, 15),
        [Unit.Stueck] = new("Stück", Dimension.Count, null),
        [Unit.Dose] = new("Dose", Dimension.Count, null),
        [Unit.Bund] = new("Bund", Dimension.Count, null),
        [Unit.Zehe] = new("Zehe", Dimension.Count, null),
        [Unit.Prise] = new("Prise", Dimension.Count, null),
        [Unit.Packung] = new("Packung", Dimension.Count, null),
        [Unit.Scheibe] = new("Scheibe", Dimension.Count, null),
    };

    private static readonly Dictionary<string, Unit> ByCode = All.ToDictionary(u => u.Value.Code, u => u.Key);

    /// <summary>The unit as it is written in a recipe, on the wire and in storage: "g", "EL", "Stück".</summary>
    public static string Code(this Unit unit) => All[unit].Code;

    public static Dimension DimensionOf(this Unit unit) => All[unit].Dimension;

    /// <summary>How many grams or millilitres one of it is; null for counted units.</summary>
    public static decimal? InBase(this Unit unit) => All[unit].InBase;

    /// <exception cref="ArgumentException">Not a known code.</exception>
    public static Unit FromCode(string code) =>
        ByCode.TryGetValue(code, out var unit) ? unit : throw new ArgumentException($"Unknown unit '{code}'.");

    /// <summary>In the order the app offers them.</summary>
    public static IReadOnlyList<Unit> InOrder { get; } = Enum.GetValues<Unit>();
}
