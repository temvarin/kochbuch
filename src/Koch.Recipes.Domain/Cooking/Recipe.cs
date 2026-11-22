using Koch.Recipes.Domain.Catalog;
using Koch.Kernel;
using Koch.Kernel.Measures;

namespace Koch.Recipes.Domain.Cooking;

/// <summary>One ingredient of a recipe: "500 g Kartoffeln, festkochend". The ingredient is named, not referenced, so
/// a line never points at something that is gone; the shopping list combines lines by <see cref="IngredientKey"/>.</summary>
public sealed record RecipeLine
{
    public const int MaxName = 80, MaxNote = 80;

    /// <param name="amount">null: "nach Geschmack", no amount.</param>
    /// <exception cref="ArgumentException">No ingredient, or texts too long.</exception>
    public RecipeLine(string ingredient, Quantity? amount, string? note = null)
    {
        Ingredient = Text.Check(Text.OneLine(ingredient), MaxName, "ingredient");
        Amount = amount;
        Note = Text.Check(Text.OneLine(note), MaxNote, "note", required: false);
    }

    public string Ingredient { get; }

    public Quantity? Amount { get; }

    /// <summary>"festkochend", "gehackt"; may be empty.</summary>
    public string Note { get; }

    /// <summary>The same line for another number of servings.</summary>
    public RecipeLine Scale(decimal factor) => new(Ingredient, Amount?.Scale(factor), Note);
}

/// <summary>
/// A recipe: what to buy for how many servings, and what to do. Immutable; every change makes a new one, which the
/// sync sends whole (<see cref="ItemChanges{T}"/>).
/// </summary>
public sealed record Recipe : IItem
{
    public const int MaxTitle = 120, MaxServings = 99, MaxLines = 100, MaxSteps = 50, MaxStep = 2000, MaxNotes = 4000;

    /// <param name="photo">The photo's id (Koch.Media.Contracts.PhotoPaths); null: none.</param>
    /// <exception cref="ArgumentException">A rule is broken: empty title, servings out of range, too many lines or steps.</exception>
    public Recipe(Guid id, string title, int servings, IEnumerable<RecipeLine> lines, IEnumerable<string> steps,
        string? notes = null, Guid? photo = null)
    {
        if (id == Guid.Empty) throw new ArgumentException("A recipe needs an id.");
        if (photo == Guid.Empty) throw new ArgumentException("A photo needs an id.");
        Photo = photo;
        if (servings is < 1 or > MaxServings) throw new ArgumentException($"Servings must be 1 to {MaxServings}.");
        Id = id;
        Title = Text.Check(Text.OneLine(title), MaxTitle, "title");
        Servings = servings;
        Lines = lines.ToValueList();
        if (Lines.Count > MaxLines) throw new ArgumentException($"A recipe has at most {MaxLines} ingredients.");
        // Empty steps are left out rather than refused: the form keeps an empty field for the next step.
        Steps = steps.Select(Text.Block).Where(step => step.Length > 0)
            .Select(step => Text.Check(step, MaxStep, "step")).ToValueList();
        if (Steps.Count > MaxSteps) throw new ArgumentException($"A recipe has at most {MaxSteps} steps.");
        Notes = Text.Check(Text.Block(notes), MaxNotes, "notes", required: false);
    }

    public Guid Id { get; }

    public string Title { get; }

    /// <summary>How many servings the amounts are for.</summary>
    public int Servings { get; }

    public ValueList<RecipeLine> Lines { get; }

    public ValueList<string> Steps { get; }

    public string Notes { get; }

    /// <summary>The photo of the dish, served by the gateway in several widths; null: none.</summary>
    public Guid? Photo { get; }

    /// <summary>The lines for <paramref name="servings"/> servings instead of <see cref="Servings"/>.</summary>
    public IReadOnlyList<RecipeLine> LinesFor(int servings) =>
        servings == Servings ? Lines : [.. Lines.Select(line => line.Scale((decimal)servings / Servings))];
}
