using System.Security.Cryptography;
using System.Text;
using Koch.Kernel;

namespace Koch.Recipes.Domain.Catalog;

/// <summary>
/// An ingredient's identity, from its name. Two lines naming the same ingredient are added up on a shopping list and
/// matched by the recipe merge, whichever phone wrote them and however they spelled the case (docs/adr/0004, 0013).
/// </summary>
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
