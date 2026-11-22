namespace Koch.Kernel;

/// <summary>
/// Where in the shop something is, in the order of a usual walk through a German supermarket, so the shopping list can
/// follow it. The wire and storage use <see cref="Aisles.Code"/>, never the number.
/// </summary>
public enum Aisle
{
    ObstGemuese,
    Brot,
    Kuehlregal,
    FleischFisch,
    Backen,
    Vorrat,
    Gewuerze,
    Tiefkuehl,
    Getraenke,
    Sonstiges,
}

public static class Aisles
{
    private static readonly Dictionary<Aisle, (string Code, string Name)> All = new()
    {
        [Aisle.ObstGemuese] = ("obst-gemuese", "Obst und Gemüse"),
        [Aisle.Brot] = ("brot", "Brot und Backwaren"),
        [Aisle.Kuehlregal] = ("kuehlregal", "Kühlregal"),
        [Aisle.FleischFisch] = ("fleisch-fisch", "Fleisch und Fisch"),
        [Aisle.Backen] = ("backen", "Backzutaten"),
        [Aisle.Vorrat] = ("vorrat", "Vorrat: Nudeln, Reis, Konserven"),
        [Aisle.Gewuerze] = ("gewuerze", "Gewürze und Öl"),
        [Aisle.Tiefkuehl] = ("tiefkuehl", "Tiefkühl"),
        [Aisle.Getraenke] = ("getraenke", "Getränke"),
        [Aisle.Sonstiges] = ("sonstiges", "Sonstiges"),
    };

    private static readonly Dictionary<string, Aisle> ByCode = All.ToDictionary(a => a.Value.Code, a => a.Key);

    public static string Code(this Aisle aisle) => All[aisle].Code;

    /// <summary>German, for the app.</summary>
    public static string Name(this Aisle aisle) => All[aisle].Name;

    /// <exception cref="ArgumentException">Not a known code.</exception>
    public static Aisle FromCode(string code) =>
        ByCode.TryGetValue(code, out var aisle) ? aisle : throw new ArgumentException($"Unknown aisle '{code}'.");

    /// <summary>In the order of the walk through the shop.</summary>
    public static IReadOnlyList<Aisle> InOrder { get; } = Enum.GetValues<Aisle>();
}
