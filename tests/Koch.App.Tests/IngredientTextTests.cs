using Koch.App.Recipes;
using Koch.Domain.Measures;
using Koch.Domain.Recipes;

namespace Koch.App.Tests;

/// <summary>An ingredient line from the form's fields: amount, a unit picked from a list, ingredient (docs/adr/0014).</summary>
public class IngredientTextTests
{
    public static TheoryData<string, Unit?, string, RecipeLine> Lines => new()
    {
        { "500", Unit.Gramm, "Kartoffeln, festkochend", new("Kartoffeln", new Quantity(500, Unit.Gramm), "festkochend") },
        { "1,5", Unit.Kilogramm, "Mehl", new("Mehl", new Quantity(1.5m, Unit.Kilogramm)) },
        { "1 ½", Unit.Essloeffel, "Öl, zum Anbraten", new("Öl", new Quantity(1.5m, Unit.Essloeffel), "zum Anbraten") },
        { "1/2", Unit.Teeloeffel, "Salz", new("Salz", new Quantity(0.5m, Unit.Teeloeffel)) },
        { "1", null, "Zwiebel", new("Zwiebel", new Quantity(1, Unit.Stueck)) },
        { "", Unit.Prise, "Salz", new("Salz", new Quantity(1, Unit.Prise)) },
        { "", null, "Salz", new("Salz", null) },
        { " ", null, "Pfeffer, nach Geschmack", new("Pfeffer", null, "nach Geschmack") },
        { "2", Unit.Glas, "мука, просеянная", new("мука", new Quantity(2, Unit.Glas), "просеянная") },
        { "500", Unit.Gramm, "картофель", new("картофель", new Quantity(500, Unit.Gramm)) },
    };

    [Theory, MemberData(nameof(Lines))]
    public void A_line_is_read_from_its_fields_in_any_language(string amount, Unit? unit, string ingredient, RecipeLine expected)
    {
        Assert.Equal(expected, IngredientText.TryRead(amount, unit, ingredient, out var error));
        Assert.Null(error);
    }

    [Theory, MemberData(nameof(Lines))]
    public void What_is_put_back_into_the_fields_reads_back_to_the_same_line(string amount, Unit? unit, string ingredient, RecipeLine line)
    {
        _ = (amount, unit, ingredient);
        var (a, u, i) = IngredientText.Fields(line);
        Assert.Equal(line, IngredientText.TryRead(a, u, i, out _));
    }

    [Theory]
    [InlineData("1,2,3", "Salz", "Menge")]
    [InlineData("1//2", "Mehl", "Menge")]
    [InlineData("0", "Salz", "Menge")]
    [InlineData("viel", "Salz", "Menge")]
    [InlineData("500", "", "Welche Zutat")]
    [InlineData("500", " , festkochend", "Welche Zutat")]
    public void What_cant_be_read_is_explained(string amount, string ingredient, string explanation)
    {
        Assert.Null(IngredientText.TryRead(amount, Unit.Gramm, ingredient, out var error));
        Assert.Contains(explanation, error, StringComparison.Ordinal);
    }
}
