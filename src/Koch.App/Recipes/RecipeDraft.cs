using Koch.Recipes.Domain.Cooking;

namespace Koch.App.Recipes;

/// <summary>A recipe as the edit form holds it while it is being typed. <see cref="TryBuild"/> turns it into a
/// <see cref="Recipe"/>, or says in German what is missing.</summary>
public sealed class RecipeDraft
{
    public sealed class StepText
    {
        public string Text { get; set; } = "";
    }

    public string Title { get; set; } = "";
    public int Servings { get; set; } = 2;

    /// <summary>The ingredient lines read so far, in order.</summary>
    public List<RecipeLine> Lines { get; } = [];

    /// <summary>The ingredient line being typed (<see cref="IngredientText"/>); it is added on Enter, or on saving.</summary>
    public string NewLine { get; set; } = "";

    // Where the line being typed goes: the place of the line taken back to change it, else the end.
    private int? editing;

    public List<StepText> Steps { get; } = [new()];
    public string Notes { get; set; } = "";

    /// <summary>The uploaded photo's id; null: none.</summary>
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

    /// <summary>Reads the line being typed and adds it; nothing happens to an empty one.</summary>
    /// <param name="error">German, for the user; null if the line was added or there was none.</param>
    public bool TryAddNewLine(out string? error)
    {
        error = null;
        if (NewLine.Trim().Length == 0) return true;
        if (IngredientText.TryParse(NewLine, out error) is not { } line) return false;
        Lines.Insert(Math.Min(editing ?? Lines.Count, Lines.Count), line); // a line removed meanwhile may have shortened the list
        (NewLine, editing) = ("", null);
        return true;
    }

    /// <summary>
    /// Takes a line back into the input to change it; it goes back to its place. The line being typed, if any, is added
    /// first.
    /// </summary>
    public bool TryEdit(RecipeLine line, out string? error)
    {
        if (!TryAddNewLine(out error)) return false;
        editing = Lines.IndexOf(line);
        Lines.RemoveAt(editing.Value);
        NewLine = IngredientText.Format(line);
        return true;
    }

    /// <param name="error">German, for the user; null if the recipe was built.</param>
    public Recipe? TryBuild(Guid id, out string? error)
    {
        if (!TryAddNewLine(out error)) return null;
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
        try
        {
            return new Recipe(id, Title, Servings, Lines, Steps.Select(s => s.Text), Notes, Photo);
        }
        catch (ArgumentException)
        {
            error = $"Zu lang: Name höchstens {Recipe.MaxTitle} Zeichen, höchstens {Recipe.MaxLines} Zutaten und "
                + $"{Recipe.MaxSteps} Schritte mit je {Recipe.MaxStep} Zeichen.";
            return null;
        }
    }
}
