using Koch.Kernel.Measures;

namespace Koch.Shopping.Domain;

/// <summary>
/// How amounts of one ingredient add up on a shopping list: 500 g + 300 g = 800 g, 1 kg + 200 g = 1,2 kg. The result
/// depends only on the amounts, never on their order (a test checks every order).
/// </summary>
public static class Amounts
{
    /// <summary>
    /// <list type="bullet">
    /// <item>Mass adds up as mass and volume as volume (TL = 5 ml, EL = 15 ml), shown in the largest sensible unit.
    /// Amounts all in one unit stay in it: 2 EL + 1 EL = 3 EL, not 45 ml.</item>
    /// <item>Counted units add up only with the same unit: 2 Dosen + 1 Dose = 3 Dosen, but 1 Bund and 2 Stück stay
    /// apart. Fractions are rounded up, because nobody buys 1,5 eggs.</item>
    /// <item>Mass and volume never mix: there is no density table, so 200 g Mehl and 100 ml Mehl stay apart.</item>
    /// </list>
    /// </summary>
    /// <returns>Mass first, then volume, then counted units in the order of <see cref="Unit"/>.</returns>
    public static IReadOnlyList<Quantity> Combine(IEnumerable<Quantity> amounts)
    {
        var all = amounts.ToList();
        List<Quantity> result = [];
        foreach (var dimension in new[] { Dimension.Mass, Dimension.Volume })
        {
            var some = all.Where(q => q.Unit.DimensionOf() == dimension).ToList();
            if (some.Count == 0) continue;
            var units = some.Select(q => q.Unit).Distinct().ToList();
            result.Add(units.Count == 1
                ? new Quantity(Sum(some.Select(q => q.Amount)), units[0]).Readable()
                : new Quantity(Sum(some.Select(q => q.Amount * q.Unit.InBase()!.Value)),
                    dimension == Dimension.Mass ? Unit.Gramm : Unit.Milliliter).Readable());
        }
        result.AddRange(all.Where(q => q.Unit.DimensionOf() == Dimension.Count)
            .GroupBy(q => q.Unit).OrderBy(g => g.Key)
            .Select(g => new Quantity(Math.Ceiling(Sum(g.Select(q => q.Amount))), g.Key)));
        return result;
    }

    // Quantity.Max per amount keeps sums in range for any realistic list; beyond it, the largest amount stands.
    private static decimal Sum(IEnumerable<decimal> amounts) => Math.Min(amounts.Sum(), Quantity.Max);
}
