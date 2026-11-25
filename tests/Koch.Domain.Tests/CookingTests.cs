namespace Koch.Domain.Tests;

public class CookingTests
{
    [Theory]
    [InlineData(1500, Unit.Gramm, 1.5, Unit.Kilogramm)]
    [InlineData(999, Unit.Gramm, 999, Unit.Gramm)]
    [InlineData(0.25, Unit.Kilogramm, 250, Unit.Gramm)]
    [InlineData(2, Unit.Kilogramm, 2, Unit.Kilogramm)]
    [InlineData(1000, Unit.Milliliter, 1, Unit.Liter)]
    [InlineData(0.5, Unit.Liter, 500, Unit.Milliliter)]
    [InlineData(300, Unit.Teeloeffel, 300, Unit.Teeloeffel)]
    [InlineData(12, Unit.Stueck, 12, Unit.Stueck)]
    public void Readable_moves_grams_and_millilitres_to_the_larger_unit_from_1000_and_back_below_1(
        decimal amount, Unit unit, decimal readableAmount, Unit readableUnit)
    {
        Assert.Equal(new Quantity(readableAmount, readableUnit), new Quantity(amount, unit).Readable());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(1_000_001)]
    public void A_quantity_is_above_zero_and_not_absurd(decimal amount)
    {
        Assert.Throws<ArgumentException>(() => new Quantity(amount, Unit.Gramm));
    }

    [Fact]
    public void Scaling_keeps_three_decimals_and_never_reaches_zero()
    {
        Assert.Equal(new Quantity(0.333m, Unit.Liter), new Quantity(1, Unit.Liter).Scale(1m / 3));
        Assert.Equal(new Quantity(0.001m, Unit.Prise), new Quantity(1, Unit.Prise).Scale(0.0001m));
    }

    [Fact]
    public void Lines_for_more_servings_scale_every_amount_and_leave_amountless_lines_alone()
    {
        var lines = Known.Soup().LinesFor(8);

        Assert.Equal(new Quantity(1000, Unit.Gramm), lines[0].Amount);
        Assert.Equal(new Quantity(2, Unit.Zehe), lines[1].Amount);
        Assert.Null(lines[3].Amount);
        Assert.Equal("festkochend", lines[0].Note);
    }

    [Fact]
    public void Recipes_with_the_same_values_are_equal_even_with_separate_lists()
    {
        Assert.Equal(Known.Soup(), Known.Soup());
        Assert.NotSame(Known.Soup().Lines, Known.Soup().Lines);
    }

    [Fact]
    public void Text_is_tidied_and_empty_steps_are_left_out()
    {
        var recipe = new Recipe(Guid.NewGuid(), "  Kartoffel   suppe ", 2,
            [new RecipeLine("  Kartoffeln ", null, "  mehlig  ")], ["  ", "Schälen.\r\nKochen.  ", ""]);

        Assert.Equal("Kartoffel suppe", recipe.Title);
        Assert.Equal("Kartoffeln", recipe.Lines[0].Ingredient);
        Assert.Equal("mehlig", recipe.Lines[0].Note);
        Assert.Equal(["Schälen.\nKochen."], recipe.Steps);
    }

    public static TheoryData<Func<Recipe>> Broken => new()
    {
        () => new Recipe(Guid.Empty, "Suppe", 2, [], []),
        () => new Recipe(Guid.NewGuid(), "  ", 2, [], []),
        () => new Recipe(Guid.NewGuid(), "Suppe", 0, [], []),
        () => new Recipe(Guid.NewGuid(), "Suppe", 100, [], []),
        () => new Recipe(Guid.NewGuid(), new string('x', 121), 2, [], []),
        () => new Recipe(Guid.NewGuid(), "Suppe", 2, Enumerable.Repeat(new RecipeLine("Salz", null), 101), []),
        () => new Recipe(Guid.NewGuid(), "Suppe", 2, [], Enumerable.Repeat("Rühren.", 51)),
        () => new Recipe(Guid.NewGuid(), "Suppe", 2, [new RecipeLine(" ", null)], []),
    };

    [Theory, MemberData(nameof(Broken))]
    public void A_recipe_that_breaks_a_rule_is_refused(Func<Recipe> create)
    {
        Assert.Throws<ArgumentException>(create);
    }

    [Fact]
    public void Every_unit_has_a_code_and_a_dimension()
    {
        Assert.All(Units.InOrder, unit => Assert.Equal(unit, Units.FromCode(unit.Code())));
        Assert.Throws<ArgumentException>(() => Units.FromCode("Eimer"));
    }
}
