using System.Collections;

namespace Koch.Domain.Kernel;

/// <summary>Rules for text the user types.</summary>
public static class Text
{
    public static string OneLine(string? text) =>
        string.Join(' ', (text ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    public static string Block(string? text) => (text ?? "").Replace("\r\n", "\n", StringComparison.Ordinal).Trim();

    public static string Check(string text, int max, string what, bool required = true)
    {
        if (required && text.Length == 0) throw new ArgumentException($"The {what} is empty.");
        if (text.Length > max) throw new ArgumentException($"The {what} is longer than {max} characters.");
        return text;
    }
}

/// <summary>An immutable list that compares equality by item sequence.</summary>
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
