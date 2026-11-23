using Koch.App.Recipes;
using Koch.Kernel.Measures;
using Koch.Recipes.Domain.Cooking;

namespace Koch.App.Tests;

/// <summary>An ingredient typed as one line, the way recipes write it (docs/adr/0014).</summary>
public class IngredientTextTests
{
    public static TheoryData<string, RecipeLine> Lines => new()
    {
        { "500 g Kartoffeln, festkochend", new("Kartoffeln", new Quantity(500, Unit.Gramm), "festkochend") },
        { "500g Kartoffeln", new("Kartoffeln", new Quantity(500, Unit.Gramm)) },
        { "1,5 kg Mehl", new("Mehl", new Quantity(1.5m, Unit.Kilogramm)) },
        { "1 ½ EL Öl, zum Anbraten", new("Öl", new Quantity(1.5m, Unit.Essloeffel), "zum Anbraten") },
        { "1/2 TL Salz", new("Salz", new Quantity(0.5m, Unit.Teeloeffel)) },
        { "2 Zehen Knoblauch", new("Knoblauch", new Quantity(2, Unit.Zehe)) },
        { "1 Dose Tomaten", new("Tomaten", new Quantity(1, Unit.Dose)) },
        { "1 Zwiebel", new("Zwiebel", new Quantity(1, Unit.Stueck)) },
        { "Prise Salz", new("Salz", new Quantity(1, Unit.Prise)) },
        { "Salz", new("Salz", null) },
        { "Pfeffer, nach Geschmack", new("Pfeffer", null, "nach Geschmack") },
        { "  200 ml   Sahne ", new("Sahne", new Quantity(200, Unit.Milliliter)) },
        { "Bund Petersilie", new("Petersilie", new Quantity(1, Unit.Bund)) },
        { "1 Liter Milch", new("Milch", new Quantity(1, Unit.Liter)) },
    };

    [Theory, MemberData(nameof(Lines))]
    public void A_line_is_read_as_recipes_write_it(string text, RecipeLine expected)
    {
        Assert.Equal(expected, IngredientText.TryParse(text, out var error));
        Assert.Null(error);
    }

    [Theory, MemberData(nameof(Lines))]
    public void What_is_shown_for_editing_reads_back_to_the_same_line(string text, RecipeLine line)
    {
        _ = text;
        Assert.Equal(line, IngredientText.TryParse(IngredientText.Format(line), out _));
    }

    [Theory]
    [InlineData("1,2,3 g Salz", "Menge")]
    [InlineData("1//2 g Mehl", "Menge")]
    [InlineData("0 g Salz", "Menge")]
    [InlineData("500 g", "Welche Zutat")]
    [InlineData("500 g , festkochend", "Welche Zutat")]
    [InlineData("1 Dose", "Welche Zutat")]
    [InlineData("500 g, festkochend", "Welche Zutat")]
    public void What_cant_be_read_is_explained(string text, string explanation)
    {
        Assert.Null(IngredientText.TryParse(text, out var error));
        Assert.Contains(explanation, error, StringComparison.Ordinal);
    }
}
