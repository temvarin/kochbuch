using System.Globalization;
using Koch.Kernel.Measures;

namespace Koch.App.Shell;

/// <summary>Numbers and amounts as a German reader expects them: "1,5 kg", "250 g", "½ TL".</summary>
public static class German
{
    private static readonly Dictionary<decimal, string> Fractions = new() { [0.25m] = "¼", [0.5m] = "½", [0.75m] = "¾" };

    /// <summary>
    /// Grams and millilitres whole from 10 on, everything else to at most two decimals. Metric units as decimals
    /// ("1,5 kg"), spoons and counted units with ¼, ½, ¾ signs ("½ TL", "1 ½ Dosen").
    /// </summary>
    public static string Amount(decimal amount, Unit unit)
    {
        var rounded = unit is Unit.Gramm or Unit.Milliliter && amount >= 10 ? Math.Round(amount) : Math.Round(amount, 2);
        if (unit is Unit.Gramm or Unit.Kilogramm or Unit.Milliliter or Unit.Liter) return Number(rounded);
        var whole = Math.Truncate(rounded);
        if (Fractions.TryGetValue(rounded - whole, out var fraction))
            return whole == 0 ? fraction : $"{Number(whole)} {fraction}";
        return Number(rounded);
    }

    public static string Quantity(Quantity quantity)
    {
        var readable = quantity.Readable();
        return $"{Amount(readable.Amount, readable.Unit)} {readable.Unit.Code()}";
    }

    /// <summary>"1,5", "1.5", "1/2", "1 1/2", "½" all read as the number they say.</summary>
    public static bool TryParseAmount(string? text, out decimal amount)
    {
        amount = 0;
        var parts = (text ?? "").Trim().Replace(',', '.').Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length is 0 or > 2) return false;
        foreach (var part in parts)
        {
            if (!TryParsePart(part, out var value)) return false;
            amount += value;
        }
        return amount > 0;
    }

    public static string Number(decimal value) => value.ToString("0.##", CultureInfo.InvariantCulture).Replace('.', ',');

    /// <summary>Every digit the amount has, for editing: what is shown is what gets saved again.</summary>
    public static string Exact(decimal value) =>
        value.ToString("0.############################", CultureInfo.InvariantCulture).Replace('.', ',');

    private static bool TryParsePart(string part, out decimal value)
    {
        if (Fractions.FirstOrDefault(f => f.Value == part) is { Value: not null } sign)
        {
            value = sign.Key;
            return true;
        }
        if (part.Split('/') is [var top, var bottom]
            && decimal.TryParse(top, NumberStyles.None, CultureInfo.InvariantCulture, out var n)
            && decimal.TryParse(bottom, NumberStyles.None, CultureInfo.InvariantCulture, out var d) && d != 0)
        {
            value = n / d;
            return true;
        }
        return decimal.TryParse(part, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out value);
    }
}
