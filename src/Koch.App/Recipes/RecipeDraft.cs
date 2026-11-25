using Koch.App.Shell;
using Koch.Domain.Measures;
using Koch.Domain.Recipes;

namespace Koch.App.Recipes;

public sealed class RecipeDraft
{
    public sealed class StepText
    {
        public string Text { get; set; } = "";
    }

    public string Title { get; set; } = "";
    public int Servings { get; set; } = 2;

    public List<RecipeLine> Lines { get; } = [];

    public string NewAmount { get; set; } = "";
    public Unit? NewUnit { get; set; }
    public string NewIngredient { get; set; } = "";

    private bool Writing => NewAmount.Trim().Length > 0 || NewUnit is not null || NewIngredient.Trim().Length > 0;

    private int? editing;

    public List<StepText> Steps { get; } = [new()];
    public string Notes { get; set; } = "";
    public Guid? Photo { get; set; }

    public static RecipeDraft From(Recipe recipe)
    {
        var draft = new RecipeDraft { Title = recipe.Title, Servings = recipe.Servings, Notes = recipe.Notes, Photo = recipe.Photo };
        draft.Lines.AddRange(recipe.Lines);
        draft.Steps.Clear();
        draft.Steps.AddRange(recipe.Steps.Select(s => new StepText { Text = s }));
        draft.Steps.Add(new());
        return draft;
    }

    public bool TryAddNewLine(out string? error, UnitLanguage language = UnitLanguage.German)
    {
        error = null;
        if (!Writing) return true;
        if (IngredientText.TryRead(NewAmount, NewUnit, NewIngredient, out error, language) is not { } line) return false;
        Lines.Insert(Math.Min(editing ?? Lines.Count, Lines.Count), line);
        (NewAmount, NewUnit, NewIngredient, editing) = ("", null, "", null);
        return true;
    }

    public bool TryEdit(RecipeLine line, out string? error, UnitLanguage language = UnitLanguage.German)
    {
        if (!TryAddNewLine(out error, language)) return false;
        editing = Lines.IndexOf(line);
        Lines.RemoveAt(editing.Value);
        (NewAmount, NewUnit, NewIngredient) = IngredientText.Fields(line);
        return true;
    }

    public Recipe? TryBuild(Guid id, out string? error, UnitLanguage language = UnitLanguage.German)
    {
        if (!TryAddNewLine(out error, language)) return null;
        var ru = language == UnitLanguage.Russian;
        if (Title.Trim().Length == 0)
        {
            error = ru ? "У рецепта должно быть название." : "Das Rezept braucht einen Namen.";
            return null;
        }
        if (Servings is < 1 or > Recipe.MaxServings)
        {
            error = ru ? $"Количество порций: от 1 до {Recipe.MaxServings}." : $"Portionen: 1 bis {Recipe.MaxServings}.";
            return null;
        }
        try
        {
            return new Recipe(id, Title, Servings, Lines, Steps.Select(s => s.Text), Notes, Photo);
        }
        catch (ArgumentException)
        {
            error = ru
                ? $"Слишком длинный текст: название до {Recipe.MaxTitle} символов, до {Recipe.MaxLines} ингредиентов и до {Recipe.MaxSteps} шагов по {Recipe.MaxStep} символов."
                : $"Zu lang: Name höchstens {Recipe.MaxTitle} Zeichen, höchstens {Recipe.MaxLines} Zutaten und {Recipe.MaxSteps} Schritte mit je {Recipe.MaxStep} Zeichen.";
            return null;
        }
    }
}
