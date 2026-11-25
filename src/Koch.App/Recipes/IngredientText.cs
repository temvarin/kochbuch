using Koch.App.Shell;
using Koch.Domain.Measures;
using Koch.Domain.Recipes;

namespace Koch.App.Recipes;

public static class IngredientText
{
    public static RecipeLine? TryRead(string amountText, Unit? unit, string ingredientText, out string? error,
        UnitLanguage language = UnitLanguage.German)
    {
        error = null;
        var ru = language == UnitLanguage.Russian;
        decimal? amount = null;
        if (amountText.Trim().Length > 0)
        {
            if (!German.TryParseAmount(amountText, out var value) || value > Quantity.Max)
            {
                error = ru
                    ? $"Не удалось распознать количество «{amountText.Trim()}». Например: 500, 1,5, ½ или 1 1/2."
                    : $"Die Menge „{amountText.Trim()}“ verstehe ich nicht. Zum Beispiel: 500, 1,5, ½ oder 1 1/2.";
                return null;
            }
            amount = value;
        }

        var comma = ingredientText.IndexOf(',', StringComparison.Ordinal);
        var (ingredient, note) = comma < 0 ? (ingredientText, "") : (ingredientText[..comma], ingredientText[(comma + 1)..]);
        if (ingredient.Trim().Length == 0)
        {
            error = ru
                ? "Укажите ингредиент. Например: картофель, молодой."
                : "Welche Zutat? Zum Beispiel: Kartoffeln, festkochend.";
            return null;
        }

        Quantity? quantity = amount is { } a ? new Quantity(a, unit ?? Unit.Stueck) : unit is { } u ? new Quantity(1, u) : null;
        try
        {
            return new RecipeLine(ingredient, quantity, note);
        }
        catch (ArgumentException)
        {
            error = ru
                ? $"Название ингредиента и пометка — не длиннее {RecipeLine.MaxName} символов."
                : $"Zutat und Hinweis höchstens je {RecipeLine.MaxName} Zeichen.";
            return null;
        }
    }

    public static (string Amount, Unit? Unit, string Ingredient) Fields(RecipeLine line) => (
        line.Amount is { } q ? German.Exact(q.Amount) : "",
        line.Amount?.Unit,
        line.Note.Length > 0 ? $"{line.Ingredient}, {line.Note}" : line.Ingredient);
}
