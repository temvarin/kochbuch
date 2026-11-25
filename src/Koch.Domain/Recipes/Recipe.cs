using System.Security.Cryptography;
using System.Text;
using Koch.Domain.Kernel;
using Koch.Domain.Measures;

namespace Koch.Domain.Recipes;

/// <summary>An ingredient's identity, derived deterministically from its normalized name.</summary>
public static class IngredientKey
{
    public static Guid Of(string name)
    {
        var normalized = Text.OneLine(name).ToLowerInvariant();
        Span<byte> hash = stackalloc byte[32];
        SHA256.HashData(Encoding.UTF8.GetBytes(normalized), hash);
        var bytes = hash[..16];
        bytes[6] = (byte)((bytes[6] & 0x0F) | 0x80); // version 8: custom
        bytes[8] = (byte)((bytes[8] & 0x3F) | 0x80); // RFC 9562 variant
        return new Guid(bytes, bigEndian: true);
    }
}

public sealed record RecipeLine
{
    public const int MaxName = 80, MaxNote = 80;

    public RecipeLine(string ingredient, Quantity? amount, string? note = null)
    {
        Ingredient = Text.Check(Text.OneLine(ingredient), MaxName, "ingredient");
        Amount = amount;
        Note = Text.Check(Text.OneLine(note), MaxNote, "note", required: false);
    }

    public string Ingredient { get; }

    public Quantity? Amount { get; }

    public string Note { get; }

    public RecipeLine Scale(decimal factor) => new(Ingredient, Amount?.Scale(factor), Note);
}

public sealed record Recipe : IItem
{
    public const int MaxTitle = 120, MaxServings = 99, MaxLines = 100, MaxSteps = 50, MaxStep = 2000, MaxNotes = 4000;

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
        Steps = steps.Select(Text.Block).Where(step => step.Length > 0)
            .Select(step => Text.Check(step, MaxStep, "step")).ToValueList();
        if (Steps.Count > MaxSteps) throw new ArgumentException($"A recipe has at most {MaxSteps} steps.");
        Notes = Text.Check(Text.Block(notes), MaxNotes, "notes", required: false);
    }

    public Guid Id { get; }

    public string Title { get; }

    public int Servings { get; }

    public ValueList<RecipeLine> Lines { get; }

    public ValueList<string> Steps { get; }

    public string Notes { get; }

    public Guid? Photo { get; }

    public IReadOnlyList<RecipeLine> LinesFor(int servings) =>
        servings == Servings ? Lines : [.. Lines.Select(line => line.Scale((decimal)servings / Servings))];
}
