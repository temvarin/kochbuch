using System.Collections;

namespace Koch.Kernel;

/// <summary>
/// An immutable list that is equal to another with the same items in the same order. Records compare their lists by
/// reference otherwise, and the sync finds changed items by comparing records (<see cref="ItemChanges"/>).
/// </summary>
public sealed class ValueList<T> : IReadOnlyList<T>, IEquatable<ValueList<T>>
{
    private readonly T[] items;

    public ValueList(IEnumerable<T> items) => this.items = [.. items];

    public T this[int index] => items[index];

    public int Count => items.Length;

    public IEnumerator<T> GetEnumerator() => ((IEnumerable<T>)items).GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => items.GetEnumerator();

    public bool Equals(ValueList<T>? other) => other is not null && items.SequenceEqual(other.items);

    public override bool Equals(object? obj) => Equals(obj as ValueList<T>);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        foreach (var item in items) hash.Add(item);
        return hash.ToHashCode();
    }
}

public static class ValueList
{
    public static ValueList<T> ToValueList<T>(this IEnumerable<T> items) => new(items);
}
