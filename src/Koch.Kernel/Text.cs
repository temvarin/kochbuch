namespace Koch.Kernel;

/// <summary>Rules for text the user types.</summary>
public static class Text
{
    /// <summary>Trimmed, with runs of white space (also line breaks) as one space.</summary>
    public static string OneLine(string? text) =>
        string.Join(' ', (text ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    /// <summary>Trimmed; line breaks are kept, but not at the start or the end.</summary>
    public static string Block(string? text) => (text ?? "").Replace("\r\n", "\n", StringComparison.Ordinal).Trim();

    /// <exception cref="ArgumentException">Longer than <paramref name="max"/>, or empty while <paramref name="required"/>.</exception>
    public static string Check(string text, int max, string what, bool required = true)
    {
        if (required && text.Length == 0) throw new ArgumentException($"The {what} is empty.");
        if (text.Length > max) throw new ArgumentException($"The {what} is longer than {max} characters.");
        return text;
    }
}
