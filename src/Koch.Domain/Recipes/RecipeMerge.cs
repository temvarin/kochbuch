using Koch.Domain.Kernel;

namespace Koch.Domain.Recipes;

/// <summary>
/// Three-way merge for concurrent edits of one recipe: part by part (title, notes, photo, steps, and ingredient lines
/// unless servings changed) against the sync base the phone sent.
/// </summary>
public static class RecipeMerge
{
    public static Recipe Merge(Recipe baseRecipe, Recipe current, Recipe incoming)
    {
        if (incoming == baseRecipe) return current;
        if (current == baseRecipe || current == incoming) return incoming;

        var (servings, lines) = baseRecipe.Servings == current.Servings && baseRecipe.Servings == incoming.Servings
            ? (incoming.Servings, MergeLines(baseRecipe.Lines, current.Lines, incoming.Lines).ToValueList())
            : Pick((baseRecipe.Servings, baseRecipe.Lines), (current.Servings, current.Lines), (incoming.Servings, incoming.Lines));
        try
        {
            return new Recipe(incoming.Id,
                Pick(baseRecipe.Title, current.Title, incoming.Title),
                servings,
                lines,
                Pick(baseRecipe.Steps, current.Steps, incoming.Steps),
                Pick(baseRecipe.Notes, current.Notes, incoming.Notes),
                Pick(baseRecipe.Photo, current.Photo, incoming.Photo));
        }
        catch (ArgumentException)
        {
            return incoming;
        }
    }

    private static T Pick<T>(T baseValue, T current, T incoming) =>
        EqualityComparer<T>.Default.Equals(incoming, baseValue) ? current : incoming;

    private static List<RecipeLine> MergeLines(
        IReadOnlyList<RecipeLine> baseLines, IReadOnlyList<RecipeLine> current, IReadOnlyList<RecipeLine> incoming)
    {
        var before = Keyed(baseLines).ToDictionary();
        var theirs = Keyed(current);
        var theirsByKey = theirs.ToDictionary();
        var mine = Keyed(incoming);
        var mineKeys = mine.Select(l => l.Key).ToHashSet();

        List<(LineKey Key, RecipeLine Line)> merged = [];
        foreach (var (key, line) in mine)
        {
            if (!before.TryGetValue(key, out var was)) merged.Add((key, line));
            else if (theirsByKey.TryGetValue(key, out var now)) merged.Add((key, Pick(was, now, line)));
            else if (line != was) merged.Add((key, line));
        }

        foreach (var (index, (key, line)) in theirs.Index())
        {
            if (mineKeys.Contains(key)) continue;
            if (before.TryGetValue(key, out var was) && line == was) continue;
            var after = theirs.Take(index).Select(l => l.Key).LastOrDefault(k => merged.Any(m => m.Key == k));
            var position = after is null ? 0 : merged.FindIndex(m => m.Key == after) + 1;
            merged.Insert(position, (key, line));
        }
        return [.. merged.Select(m => m.Line)];
    }

    private sealed record LineKey(Guid Ingredient, int Occurrence);

    private static List<(LineKey Key, RecipeLine Line)> Keyed(IEnumerable<RecipeLine> lines)
    {
        Dictionary<Guid, int> seen = [];
        List<(LineKey, RecipeLine)> keyed = [];
        foreach (var line in lines)
        {
            var ingredient = IngredientKey.Of(line.Ingredient);
            var occurrence = seen.GetValueOrDefault(ingredient);
            seen[ingredient] = occurrence + 1;
            keyed.Add((new LineKey(ingredient, occurrence), line));
        }
        return keyed;
    }
}
