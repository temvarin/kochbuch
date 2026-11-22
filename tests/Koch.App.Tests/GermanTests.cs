using Koch.App.Shell;
using Koch.Kernel.Measures;

namespace Koch.App.Tests;

public class GermanTests
{
    [Theory]
    [InlineData("500", 500)]
    [InlineData("1,5", 1.5)]
    [InlineData("1.5", 1.5)]
    [InlineData("1/2", 0.5)]
    [InlineData("1 1/2", 1.5)]
    [InlineData("½", 0.5)]
    [InlineData(" 2 ", 2)]
    public void Amounts_are_read_as_people_type_them(string text, decimal expected)
    {
        Assert.True(German.TryParseAmount(text, out var amount));
        Assert.Equal(expected, amount);
    }

    [Theory]
    [InlineData("")]
    [InlineData("viel")]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("1/0")]
    [InlineData("1 2 3")]
    public void Anything_else_is_not_an_amount(string text)
    {
        Assert.False(German.TryParseAmount(text, out _));
    }

    [Theory]
    [InlineData(1500, Unit.Gramm, "1,5 kg")]
    [InlineData(333.333, Unit.Gramm, "333 g")]
    [InlineData(0.5, Unit.Teeloeffel, "½ TL")]
    [InlineData(1.75, Unit.Liter, "1,75 l")]
    [InlineData(1.5, Unit.Dose, "1 ½ Dose")]
    [InlineData(2.333, Unit.Stueck, "2,33 Stück")]
    public void Quantities_read_the_way_german_recipes_write_them(decimal amount, Unit unit, string expected)
    {
        Assert.Equal(expected, German.Quantity(new Quantity(amount, unit)));
    }
}
