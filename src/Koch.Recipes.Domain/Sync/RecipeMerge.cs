using Koch.Kernel;
using Koch.Recipes.Domain.Catalog;
using Koch.Recipes.Domain.Cooking;

namespace Koch.Recipes.Domain.Sync;

/// <summary>
/// Merges two concurrent edits of one recipe: the service's version and a phone's, both made from the base the phone
/// last saw (docs/adr/0012). A recipe is treated as a map of parts, each merged on its own (DDIA ch06 p.226-227):
/// <list type="bullet">
/// <item>title, notes, photo and the steps: the side that changed the part gives it; if both did, the phone's (it
/// arrives last);</item>
/// <item>the ingredient lines: a collection, merged line by line, with removals tracked against the base so a removed
/// line doesn't come back (the shopping-cart anomaly, DDIA p.225);</item>
/// <item>servings and amounts belong together: once either side changed the servings, the lines are not mixed, and the
/// side that changed servings or lines last gives both.</item>
/// </list>
/// The service merges in the order requests arrive, so every phone ends up with the same recipe.
/// </summary>
public static class RecipeMerge
{
    /// <param name="baseRecipe">The recipe as the phone last got it from the service.</param>
    /// <param name="current">The recipe as the service has it now.</param>
    /// <param name="incoming">The recipe as the phone sends it now.</param>
    public static Recipe Merge(Recipe baseRecipe, Recipe current, Recipe incoming)
    {
        if (incoming == baseRecipe) return current; // nothing changed on the phone: a resend, or a stale copy
        if (current == baseRecipe || current == incoming) return incoming; // nothing concurrent to keep

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
            // Both sides added lines and together they pass the limit: no merge can keep both (DDIA p.227). The phone's
            // recipe is a valid one, so it wins whole, as before merging existed.
            return incoming;
        }
    }

    /// <summary>The phone's value if it changed the part, else the service's.</summary>
    private static T Pick<T>(T baseValue, T current, T incoming) =>
        EqualityComparer<T>.Default.Equals(incoming, baseValue) ? current : incoming;

    /// <summary>
    /// A line is known by its ingredient and how many lines of that ingredient come before it (a recipe may need butter
    /// twice). Lines the phone has come in its order; lines only the service has go after the line they follow there.
    /// </summary>
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
            if (!before.TryGetValue(key, out var was)) merged.Add((key, line)); // added on the phone
            else if (theirsByKey.TryGetValue(key, out var now)) merged.Add((key, Pick(was, now, line)));
            else if (line != was) merged.Add((key, line)); // removed on the service, edited on the phone: the edit stays
        }

        foreach (var (index, (key, line)) in theirs.Index())
        {
            if (mineKeys.Contains(key)) continue;
            // Removed on the phone and untouched on the service: stays removed. Edited there, or added there: kept.
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
