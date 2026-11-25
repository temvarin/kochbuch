namespace Koch.Domain.Tests;

public class AmountsTests
{
    private static Quantity Q(decimal amount, Unit unit) => new(amount, unit);

    public static TheoryData<Quantity[], Quantity[]> Cases => new()
    {
        { [Q(500, Unit.Gramm), Q(300, Unit.Gramm)], [Q(800, Unit.Gramm)] },
        { [Q(1, Unit.Kilogramm), Q(200, Unit.Gramm)], [Q(1.2m, Unit.Kilogramm)] },
        { [Q(600, Unit.Gramm), Q(600, Unit.Gramm)], [Q(1.2m, Unit.Kilogramm)] },
        { [Q(0.5m, Unit.Liter), Q(250, Unit.Milliliter)], [Q(750, Unit.Milliliter)] },
        { [Q(2, Unit.Essloeffel), Q(1, Unit.Essloeffel)], [Q(3, Unit.Essloeffel)] },
        { [Q(1, Unit.Essloeffel), Q(1, Unit.Teeloeffel)], [Q(20, Unit.Milliliter)] },
        { [Q(1, Unit.Glas), Q(0.5m, Unit.Glas)], [Q(1.5m, Unit.Glas)] },
        { [Q(1, Unit.Glas), Q(100, Unit.Milliliter)], [Q(350, Unit.Milliliter)] },
        { [Q(4, Unit.Glas), Q(1, Unit.Essloeffel)], [Q(1.015m, Unit.Liter)] },
        { [Q(2, Unit.Dose), Q(1, Unit.Dose)], [Q(3, Unit.Dose)] },
        { [Q(1.5m, Unit.Stueck), Q(0.25m, Unit.Stueck)], [Q(2, Unit.Stueck)] },
        { [Q(1, Unit.Bund), Q(2, Unit.Stueck)], [Q(2, Unit.Stueck), Q(1, Unit.Bund)] },
        { [Q(200, Unit.Gramm), Q(100, Unit.Milliliter), Q(1, Unit.Prise)], [Q(200, Unit.Gramm), Q(100, Unit.Milliliter), Q(1, Unit.Prise)] },
        { [], [] },
    };

    [Theory, MemberData(nameof(Cases))]
    public void Amounts_of_one_ingredient_add_up_as_a_shopper_expects(Quantity[] amounts, Quantity[] expected)
    {
        Assert.Equal(expected, Amounts.Combine(amounts));
    }

    [Fact]
    public void The_order_never_changes_the_result()
    {
        Unit[] units = [Unit.Gramm, Unit.Kilogramm, Unit.Milliliter, Unit.Liter, Unit.Teeloeffel, Unit.Essloeffel, Unit.Glas, Unit.Stueck, Unit.Dose];
        for (var seed = 0; seed < 500; seed++)
        {
            var random = new Random(seed);
            var amounts = Enumerable.Range(0, random.Next(1, 7))
                .Select(_ => Q(random.Next(1, 4000) / 8m, units[random.Next(units.Length)])).ToList();
            var expected = Amounts.Combine(amounts);

            for (var shuffle = 0; shuffle < 5; shuffle++)
                Assert.Equal(expected, Amounts.Combine(amounts.OrderBy(_ => random.Next())));
        }
    }
}
