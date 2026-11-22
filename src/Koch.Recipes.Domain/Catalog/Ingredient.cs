using System.Security.Cryptography;
using System.Text;
using Koch.Kernel;

namespace Koch.Recipes.Domain.Catalog;

/// <summary>
/// An ingredient both phones know, with where to find it in the shop. Its id is derived from its name, so when both
/// phones add "Kartoffeln" while offline, they add the same item, and the sync merges them instead of keeping two that
/// the shopping list would not add up (docs/adr/0004).
/// </summary>
public sealed record Ingredient : IItem
{
    /// <exception cref="ArgumentException">No name, or too long.</exception>
    public Ingredient(string name, Aisle aisle)
    {
        Name = Text.Check(Text.OneLine(name), Cooking.RecipeLine.MaxName, "ingredient");
        if (!Enum.IsDefined(aisle)) throw new ArgumentException($"Unknown aisle {aisle}.");
        Aisle = aisle;
    }

    public string Name { get; }

    public Aisle Aisle { get; }

    public Guid Id => IngredientKey.Of(Name);
}

public static class IngredientKey
{
    /// <summary>
    /// The same for names that differ only in case or spacing: " Kartoffeln" and "kartoffeln" are one ingredient. A
    /// name-based UUID (version 8, from SHA-256), so every device computes it alone.
    /// </summary>
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
