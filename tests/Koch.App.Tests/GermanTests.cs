using Koch.App.Shell;
using Koch.Domain.Measures;

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
    [InlineData(1.5, Unit.Dose, "1 ½ Dosen")]
    [InlineData(1, Unit.Dose, "1 Dose")]
    [InlineData(0.5, Unit.Glas, "½ Glas")]
    [InlineData(2, Unit.Glas, "2 Gläser")]
    [InlineData(2.333, Unit.Stueck, "2,33 Stück")]
    public void Quantities_read_the_way_german_recipes_write_them(decimal amount, Unit unit, string expected)
    {
        Assert.Equal(expected, new UnitNames(new MemoryStore()).Quantity(new Quantity(amount, unit)));
    }

    [Theory]
    [InlineData(1500, Unit.Gramm, "1,5 кг")]
    [InlineData(0.5, Unit.Teeloeffel, "½ ч. л.")]
    [InlineData(2, Unit.Essloeffel, "2 ст. л.")]
    [InlineData(1, Unit.Glas, "1 стакан")]
    [InlineData(1.5, Unit.Glas, "1 ½ стакана")]
    [InlineData(3, Unit.Dose, "3 банки")]
    [InlineData(5, Unit.Dose, "5 банок")]
    [InlineData(11, Unit.Zehe, "11 зубчиков")]
    [InlineData(21, Unit.Zehe, "21 зубчик")]
    [InlineData(22, Unit.Bund, "22 пучка")]
    [InlineData(4, Unit.Stueck, "4 шт.")]
    public void On_a_phone_set_to_russian_quantities_read_the_way_russian_recipes_write_them(decimal amount, Unit unit, string expected)
    {
        var names = new UnitNames(new MemoryStore());
        names.Choose(UnitLanguage.Russian);

        Assert.Equal(expected, names.Quantity(new Quantity(amount, unit)));
    }

    [Fact]
    public void The_phones_choice_is_kept()
    {
        var store = new MemoryStore();
        new UnitNames(store).Choose(UnitLanguage.Russian);

        Assert.Equal(UnitLanguage.Russian, new UnitNames(store).Language);
        Assert.Equal(UnitLanguage.German, new UnitNames(new MemoryStore()).Language);
    }
}
