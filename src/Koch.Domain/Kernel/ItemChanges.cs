namespace Koch.Domain.Kernel;

/// <summary>
/// The items of one list that were added, edited or removed since the last sync. Changed items travel whole ("recipe X
/// is now this"), never as edits, so applying the same changes twice gives the same result, and a request repeated
/// after a timeout does no harm.
/// </summary>
/// <param name="Changed">Added or edited.</param>
/// <param name="Removed">The ids of removed items.</param>
public sealed record ItemChanges<T>(IReadOnlyList<T> Changed, IReadOnlyList<Guid> Removed) where T : IItem
{
    public bool IsEmpty => Changed.Count == 0 && Removed.Count == 0;

    /// <summary>
    /// Edited items replace the item with the same id; the others are added at the end. An item edited on one device
    /// and removed on another comes back if the edit arrives last: the change that arrives last wins.
    /// </summary>
    public IReadOnlyList<T> ApplyTo(IReadOnlyList<T> items)
    {
        var edited = Changed.ToDictionary(i => i.Id);
        var removed = Removed.ToHashSet();
        List<T> kept = [.. items.Where(i => !removed.Contains(i.Id)).Select(i => edited.GetValueOrDefault(i.Id, i))];
        var keptIds = kept.Select(i => i.Id).ToHashSet();
        return [.. kept, .. Changed.Where(i => !keptIds.Contains(i.Id))];
    }

    /// <summary>For an item in both, this one's version.</summary>
    public ItemChanges<T> Union(ItemChanges<T> other) =>
        new([.. Changed.UnionBy(other.Changed, i => i.Id)], [.. Removed.Union(other.Removed)]);

    /// <summary>The same items as they are now: changed if still in <paramref name="items"/>, removed if not.</summary>
    public ItemChanges<T> Now(IReadOnlyList<T> items)
    {
        var ids = Changed.Select(i => i.Id).Concat(Removed).ToHashSet();
        var present = items.Where(i => ids.Contains(i.Id)).ToList();
        return new(present, [.. ids.Except(present.Select(i => i.Id))]);
    }
}

public static class ItemChanges
{
    public static ItemChanges<T> None<T>() where T : IItem => new([], []);

    public static ItemChanges<T> Between<T>(IReadOnlyList<T> before, IReadOnlyList<T> after) where T : IItem =>
        new([.. after.Except(before)], [.. before.Select(i => i.Id).Except(after.Select(i => i.Id))]);
}
