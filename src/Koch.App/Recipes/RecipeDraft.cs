using Koch.App.Shell;
using Koch.Kernel.Measures;
using Koch.Recipes.Domain.Cooking;

namespace Koch.App.Recipes;

/// <summary>A recipe as the edit form holds it: text the user is still typing. <see cref="TryBuild"/> turns it into a
/// <see cref="Recipe"/>, or says in German what is missing.</summary>
public sealed class RecipeDraft
{
    public sealed class Line
    {
        /// <summary>Empty: no amount ("nach Geschmack").</summary>
        public string Amount { get; set; } = "";
        public Unit Unit { get; set; } = Unit.Gramm;
        public string Ingredient { get; set; } = "";
        public string Note { get; set; } = "";

        public bool IsBlank => Amount.Trim().Length == 0 && Ingredient.Trim().Length == 0 && Note.Trim().Length == 0;
    }

    public sealed class StepText
    {
        public string Text { get; set; } = "";
    }

    public string Title { get; set; } = "";
    public int Servings { get; set; } = 2;
    public List<Line> Lines { get; } = [new()];
    public List<StepText> Steps { get; } = [new()];
    public string Notes { get; set; } = "";

    /// <summary>The uploaded photo's id; null: none.</summary>
    public Guid? Photo { get; set; }

    public static RecipeDraft From(Recipe recipe)
    {
        var draft = new RecipeDraft { Title = recipe.Title, Servings = recipe.Servings, Notes = recipe.Notes, Photo = recipe.Photo };
        draft.Lines.Clear();
        draft.Lines.AddRange(recipe.Lines.Select(l => new Line
        {
            Amount = l.Amount is { } q ? German.Exact(q.Amount) : "", // not rounded: saving unchanged must not change it
            Unit = l.Amount?.Unit ?? Unit.Gramm,
            Ingredient = l.Ingredient,
            Note = l.Note,
        }));
        draft.Lines.Add(new());
        draft.Steps.Clear();
        draft.Steps.AddRange(recipe.Steps.Select(s => new StepText { Text = s }));
        draft.Steps.Add(new());
        return draft;
    }

    /// <summary>Lines left completely empty are skipped: the form always offers an empty one at the end.</summary>
    /// <param name="error">German, for the user; null if the recipe was built.</param>
    public Recipe? TryBuild(Guid id, out string? error)
    {
        error = null;
        if (Title.Trim().Length == 0)
        {
            error = "Das Rezept braucht einen Namen.";
            return null;
        }
        if (Servings is < 1 or > Recipe.MaxServings)
        {
            error = $"Portionen: 1 bis {Recipe.MaxServings}.";
            return null;
        }

        List<RecipeLine> lines = [];
        foreach (var (line, number) in Lines.Select((l, i) => (l, i + 1)).Where(x => !x.l.IsBlank))
        {
            if (line.Ingredient.Trim().Length == 0)
            {
                error = $"Zutat {number}: Was ist es?";
                return null;
            }
            Quantity? amount = null;
            if (line.Amount.Trim().Length > 0)
            {
                if (!German.TryParseAmount(line.Amount, out var value) || value > Quantity.Max)
                {
                    error = $"Zutat {number}: Die Menge „{line.Amount.Trim()}“ verstehe ich nicht. Zum Beispiel: 500, 1,5 oder 1/2.";
                    return null;
                }
                amount = new Quantity(value, line.Unit);
            }
            try
            {
                lines.Add(new RecipeLine(line.Ingredient, amount, line.Note));
            }
            catch (ArgumentException)
            {
                error = $"Zutat {number}: Name und Hinweis höchstens je {RecipeLine.MaxName} Zeichen.";
                return null;
            }
        }

        try
        {
            return new Recipe(id, Title, Servings, lines, Steps.Select(s => s.Text), Notes, Photo);
        }
        catch (ArgumentException)
        {
            error = $"Zu lang: Name höchstens {Recipe.MaxTitle} Zeichen, höchstens {Recipe.MaxLines} Zutaten und "
                + $"{Recipe.MaxSteps} Schritte mit je {Recipe.MaxStep} Zeichen.";
            return null;
        }
    }
}
