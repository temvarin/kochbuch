using Koch.Domain.Measures;

namespace Koch.Domain.Shopping;

public static class Amounts
{
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

    private static decimal Sum(IEnumerable<decimal> amounts) => Math.Min(amounts.Sum(), Quantity.Max);
}
