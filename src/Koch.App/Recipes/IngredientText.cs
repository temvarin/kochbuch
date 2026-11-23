using System.Text.RegularExpressions;
using Koch.App.Shell;
using Koch.Kernel.Measures;
using Koch.Recipes.Domain.Cooking;

namespace Koch.App.Recipes;

/// <summary>
/// An ingredient line as people type it: "500 g Kartoffeln, festkochend", "1 ½ EL Öl", "2 Zehen Knoblauch",
/// "1 Zwiebel", "Salz". Amount, unit, ingredient, and after the first comma a note; only the ingredient is required
/// (docs/adr/0014).
/// </summary>
public static partial class IngredientText
{
    // A number ("500", "1,5", "1/2", "½") or a whole number and a fraction ("1 1/2", "1 ½"), then a space, a letter
    // ("500g") or the end: "1,2,3 g" or "1//2" are not amounts.
    [GeneratedRegex(@"^(?<amount>(?:\d+(?:[.,]\d+)?|\d+/\d+|[¼½¾])(?:\s+(?:\d+/\d+|[¼½¾]))?)(?=\s|$|\p{L})\s*(?<rest>.*)$")]
    private static partial Regex Amount();

    // Every way of writing a unit that the line accepts, beyond its code ("g", "EL", "Zehe" …). Case is ignored.
    private static readonly Dictionary<string, Unit> UnitWords = BuildUnitWords();

    /// <param name="error">German, for the user; null if the line was read.</param>
    public static RecipeLine? TryParse(string text, out string? error)
    {
        error = null;
        var rest = text.Trim();
        decimal? value = null;
        if (rest.Length > 0 && (char.IsAsciiDigit(rest[0]) || "¼½¾".Contains(rest[0], StringComparison.Ordinal)))
        {
            var match = Amount().Match(rest);
            if (!match.Success || !German.TryParseAmount(match.Groups["amount"].Value, out var amount) || amount > Quantity.Max)
            {
                error = $"Die Menge in „{rest}“ verstehe ich nicht. Zum Beispiel: 500 g Mehl, 1,5 l Milch, ½ TL Salz.";
                return null;
            }
            (value, rest) = (amount, match.Groups["rest"].Value.Trim());
        }

        Unit? unit = null;
        var words = rest.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
        if (words.Length > 0 && UnitWords.TryGetValue(words[0].TrimEnd(','), out var named))
        {
            // "500 g" or "1 Dose" alone, or "500 g, festkochend": the unit is there, the ingredient is missing.
            (unit, rest) = (named, (words[0].EndsWith(',') ? ", " : "") + (words.Length == 2 ? words[1] : ""));
        }

        var comma = rest.IndexOf(',', StringComparison.Ordinal);
        var (ingredient, note) = comma < 0 ? (rest, "") : (rest[..comma], rest[(comma + 1)..]);
        if (ingredient.Trim().Length == 0)
        {
            error = "Welche Zutat? Zum Beispiel: 500 g Kartoffeln, festkochend.";
            return null;
        }

        // "1 Zwiebel" counts pieces; "Prise Salz" is one Prise.
        Quantity? quantity = value is { } v ? new Quantity(v, unit ?? Unit.Stueck) : unit is { } u ? new Quantity(1, u) : null;
        try
        {
            return new RecipeLine(ingredient, quantity, note);
        }
        catch (ArgumentException)
        {
            error = $"Zutat und Hinweis höchstens je {RecipeLine.MaxName} Zeichen.";
            return null;
        }
    }

    /// <summary>The line as it would be typed, so that reading it again gives the same line.</summary>
    public static string Format(RecipeLine line)
    {
        var amount = line.Amount switch
        {
            null => "",
            { Unit: Unit.Stueck } q => $"{German.Exact(q.Amount)} ",
            var q => $"{German.Exact(q.Amount)} {q.Unit.Code()} ",
        };
        return $"{amount}{line.Ingredient}{(line.Note.Length > 0 ? $", {line.Note}" : "")}";
    }

    private static Dictionary<string, Unit> BuildUnitWords()
    {
        var words = Units.InOrder.ToDictionary(u => u.Code(), u => u, StringComparer.OrdinalIgnoreCase);
        foreach (var (word, unit) in new (string, Unit)[]
        {
            ("Gramm", Unit.Gramm), ("Kilo", Unit.Kilogramm), ("Kilogramm", Unit.Kilogramm), ("Milliliter", Unit.Milliliter),
            ("Liter", Unit.Liter), ("Teelöffel", Unit.Teeloeffel), ("Esslöffel", Unit.Essloeffel), ("Stk", Unit.Stueck),
            ("Stk.", Unit.Stueck), ("Dosen", Unit.Dose), ("Zehen", Unit.Zehe), ("Prisen", Unit.Prise), ("Pck", Unit.Packung),
            ("Pck.", Unit.Packung), ("Packungen", Unit.Packung), ("Scheiben", Unit.Scheibe),
        })
        {
            words[word] = unit;
        }
        return words;
    }
}
