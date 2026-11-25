using Koch.App.Sync;

namespace Koch.App.Shell;

/// <summary>
/// Per-phone UI copy in German and Russian, following <see cref="UnitNames.Language"/>.
/// Stored in localStorage under <c>koch.units</c>; recipes and shopping lists stay language-neutral.
/// </summary>
public sealed class AppText(UnitNames names)
{
    public UnitLanguage Language => names.Language;

    public bool IsRussian => names.Language == UnitLanguage.Russian;

    public string LangCode => IsRussian ? "ru" : "de";

    public string Pick(string de, string ru) => IsRussian ? ru : de;

    public static string PluralRu(long n, string one, string few, string many)
    {
        var abs = Math.Abs(n);
        var (last, lastTwo) = (abs % 10, abs % 100);
        if (last == 1 && lastTwo != 11) return one;
        if (last is >= 2 and <= 4 && lastTwo is < 12 or > 14) return few;
        return many;
    }

    public string Servings(int n) => IsRussian
        ? $"{n} {PluralRu(n, "порция", "порции", "порций")}"
        : n == 1 ? "1 Portion" : $"{n} Portionen";

    public string OpenItems(int n) => IsRussian
        ? (n == 0 ? "пусто" : $"осталось {n}")
        : (n == 0 ? "leer" : $"{n} offen");

    public string StepOf(int current, int total) => IsRussian
        ? $"Шаг {current} из {total}"
        : $"Schritt {current} von {total}";

    public string StepLabel(int number) => IsRussian
        ? $"Шаг {number}"
        : $"Schritt {number}";

    public string InTrolley(int count) => IsRussian
        ? $"В корзине · {count}"
        : $"Im Wagen · {count}";

    public string ForRecipes(IReadOnlyList<string> recipes) => IsRussian
        ? $"Для: {string.Join(", ", recipes)}"
        : $"Für {string.Join(", ", recipes)}";

    public string ForRecipesInline(IReadOnlyList<string> recipes) => IsRussian
        ? $"для: {string.Join(", ", recipes)}"
        : $"für {string.Join(", ", recipes)}";

    public string PhotoAlt(string title) => IsRussian
        ? $"Фото: {title}"
        : $"Foto: {title}";

    public string EditLabel(string shown) => IsRussian
        ? $"Изменить: {shown}"
        : $"Ändern: {shown}";

    public string RemoveLabel(string ingredient) => IsRussian
        ? $"Удалить: {ingredient}"
        : $"Entfernen: {ingredient}";

    public string PageTitle(string title) => IsRussian
        ? $"{title} — Kochbuch"
        : $"{title} – Kochbuch";

    public string CookPageTitle(string title) => IsRussian
        ? $"Готовим: {title} — Kochbuch"
        : $"Kochen: {title} – Kochbuch";

    public string DescribeSync(SyncOverview sync, DateTimeOffset now) => sync.Describe(now, Language);
}
