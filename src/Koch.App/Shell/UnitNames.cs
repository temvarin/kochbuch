using Koch.Domain.Kernel;
using Koch.Domain.Measures;

namespace Koch.App.Shell;

/// <summary>The languages a phone can show units in.</summary>
public enum UnitLanguage
{
    German,
    Russian,
}

/// <summary>
/// Units as this phone shows them, in German or in Russian, chosen under Konto and kept on the phone (localStorage key
/// koch.units). Recipes and lists store a unit as its code (Koch.Domain.Measures.Units), in no particular language,
/// so the choice changes nothing that syncs: each phone may show the same recipe in its own words.
/// </summary>
public sealed class UnitNames(IKeyValueStore store)
{
    private const string Key = "koch.units";

    // One, few, many: German needs two of them ("1 Dose", "2 Dosen"), Russian three ("1 банка", "2 банки", "5 банок").
    private sealed record Forms(string One, string Few, string Many)
    {
        public Forms(string word) : this(word, word, word) { }
    }

    private static readonly Dictionary<Unit, Forms> GermanNames = new()
    {
        [Unit.Gramm] = new("g"),
        [Unit.Kilogramm] = new("kg"),
        [Unit.Milliliter] = new("ml"),
        [Unit.Liter] = new("l"),
        [Unit.Teeloeffel] = new("TL"),
        [Unit.Essloeffel] = new("EL"),
        [Unit.Stueck] = new("Stück"),
        [Unit.Dose] = new("Dose", "Dosen", "Dosen"),
        [Unit.Bund] = new("Bund"),
        [Unit.Zehe] = new("Zehe", "Zehen", "Zehen"),
        [Unit.Prise] = new("Prise", "Prisen", "Prisen"),
        [Unit.Packung] = new("Packung", "Packungen", "Packungen"),
        [Unit.Scheibe] = new("Scheibe", "Scheiben", "Scheiben"),
        [Unit.Glas] = new("Glas", "Gläser", "Gläser"),
    };

    // Abbreviations as Russian recipes write them; the spoons with a no-break space, so "ч. л." never breaks apart.
    private static readonly Dictionary<Unit, Forms> RussianNames = new()
    {
        [Unit.Gramm] = new("г"),
        [Unit.Kilogramm] = new("кг"),
        [Unit.Milliliter] = new("мл"),
        [Unit.Liter] = new("л"),
        [Unit.Teeloeffel] = new("ч. л."),
        [Unit.Essloeffel] = new("ст. л."),
        [Unit.Stueck] = new("шт."),
        [Unit.Dose] = new("банка", "банки", "банок"),
        [Unit.Bund] = new("пучок", "пучка", "пучков"),
        [Unit.Zehe] = new("зубчик", "зубчика", "зубчиков"),
        [Unit.Prise] = new("щепотка", "щепотки", "щепоток"),
        [Unit.Packung] = new("упаковка", "упаковки", "упаковок"),
        [Unit.Scheibe] = new("ломтик", "ломтика", "ломтиков"),
        [Unit.Glas] = new("стакан", "стакана", "стаканов"),
    };

    public UnitLanguage Language { get; private set; } = store.Read(Key) == "ru" ? UnitLanguage.Russian : UnitLanguage.German;

    public event Action? Changed;

    public void Choose(UnitLanguage language)
    {
        if (Language == language) return;
        Language = language;
        store.Write(Key, language == UnitLanguage.Russian ? "ru" : "de");
        Changed?.Invoke();
    }

    /// <summary>The unit's name on its own, as the unit list offers it: "Dose", "банка".</summary>
    public string Name(Unit unit) => Names[unit].One;

    /// <summary>The unit after an amount: "2 Dosen", "1,5 стакана", "5 банок".</summary>
    public string Name(Unit unit, decimal amount) => Pick(Names[unit], amount);

    /// <summary>A quantity as this phone shows it: "1,5 kg", "½ TL", "2 банки".</summary>
    public string Quantity(Quantity quantity)
    {
        var readable = quantity.Readable();
        return $"{German.Amount(readable.Amount, readable.Unit)} {Name(readable.Unit, readable.Amount)}";
    }

    private Dictionary<Unit, Forms> Names => Language == UnitLanguage.Russian ? RussianNames : GermanNames;

    private string Pick(Forms forms, decimal amount)
    {
        if (Language == UnitLanguage.German) return amount <= 1 ? forms.One : forms.Few;
        // Russian: a fraction takes the genitive singular ("1,5 стакана", "½ стакана"); whole numbers go by their end:
        // 1, 21 банка; 2–4, 22–24 банки; 5–20, 25–30 банок.
        if (amount != decimal.Truncate(amount)) return forms.Few;
        var (last, lastTwo) = ((long)amount % 10, (long)amount % 100);
        if (last == 1 && lastTwo != 11) return forms.One;
        if (last is >= 2 and <= 4 && lastTwo is < 12 or > 14) return forms.Few;
        return forms.Many;
    }
}
