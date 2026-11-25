namespace Koch.Domain.Measures;

/// <summary>The units a recipe line can use. The wire and storage use <see cref="Units.Code"/>, never the enum number.</summary>
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
    /// <summary>A glass, стакан, as Russian recipes measure: 250 ml.</summary>
    Glas,
}

public enum Dimension
{
    Mass,
    Volume,
    Count,
}

public static class Units
{
    private sealed record Info(string Code, Dimension Dimension, decimal? InBase);

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
        [Unit.Glas] = new("Glas", Dimension.Volume, 250),
    };

    private static readonly Dictionary<string, Unit> ByCode = All.ToDictionary(u => u.Value.Code, u => u.Key);

    public static string Code(this Unit unit) => All[unit].Code;

    public static Dimension DimensionOf(this Unit unit) => All[unit].Dimension;

    public static decimal? InBase(this Unit unit) => All[unit].InBase;

    public static Unit FromCode(string code) =>
        ByCode.TryGetValue(code, out var unit) ? unit : throw new ArgumentException($"Unknown unit '{code}'.");

    public static IReadOnlyList<Unit> InOrder { get; } = Enum.GetValues<Unit>();
}
