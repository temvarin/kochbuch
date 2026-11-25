using Koch.App.Account;
using Koch.App.Shell;

namespace Koch.App.Sync;

public sealed class SyncOverview
{
    private readonly AccountService account;
    private readonly RecipeSync recipes;
    private readonly ShoppingSync shopping;

    public SyncOverview(AccountService account, RecipeSync recipes, ShoppingSync shopping)
    {
        (this.account, this.recipes, this.shopping) = (account, recipes, shopping);
        recipes.Changed += () => Changed?.Invoke();
        shopping.Changed += () => Changed?.Invoke();
    }

    public event Action? Changed;

    public SyncStatus Status => new[] { recipes.Status, shopping.Status }.OrderByDescending(Weight).First();

    public bool CanSync => recipes.CanSync || shopping.CanSync;

    public bool HasUnsyncedChanges => recipes.HasUnsyncedChanges || shopping.HasUnsyncedChanges;

    public DateTimeOffset? LastSync => recipes.LastSync is { } a && shopping.LastSync is { } b ? (a < b ? a : b) : null;

    public async Task StartAsync()
    {
        await account.CheckAsync();
        await Task.WhenAll(recipes.StartAsync(), shopping.StartAsync());
    }

    public async Task ResumeAsync()
    {
        if (account.Status is AccountStatus.Unreachable or AccountStatus.Unknown) await account.CheckAsync();
        await Task.WhenAll(recipes.ResumeAsync(), shopping.ResumeAsync());
    }

    public Task SyncNowAsync() => Task.WhenAll(recipes.SyncNowAsync(), shopping.SyncNowAsync());

    public string Describe(DateTimeOffset now, UnitLanguage language = UnitLanguage.German)
    {
        if (language == UnitLanguage.Russian)
        {
            return Status switch
            {
                SyncStatus.Syncing => "Синхронизация…",
                SyncStatus.Failed => "Не удалось синхронизировать, скоро повторим",
                SyncStatus.Outdated => "Вышла новая версия — обновите страницу",
                SyncStatus.Refused => "Синхронизация отклонена — обновите страницу",
                _ when HasUnsyncedChanges => "Есть несинхронизированные изменения",
                _ when LastSync is { } last => $"Синхронизировано · {SinceRu(now - last)}",
                _ => "Ещё не синхронизировалось",
            };
        }
        return Status switch
        {
            SyncStatus.Syncing => "Abgleich läuft …",
            SyncStatus.Failed => "Abgleich fehlgeschlagen, neuer Versuch gleich",
            SyncStatus.Outdated => "Neue Version – bitte neu laden",
            SyncStatus.Refused => "Abgleich abgelehnt – bitte neu laden",
            _ when HasUnsyncedChanges => "Änderungen warten auf den Abgleich",
            _ when LastSync is { } last => $"Abgeglichen · {SinceDe(now - last)}",
            _ => "Noch nie abgeglichen",
        };
    }

    private static string SinceDe(TimeSpan ago) => ago switch
    {
        { TotalMinutes: < 1 } => "gerade eben",
        { TotalHours: < 1 } => $"vor {(int)ago.TotalMinutes} Min.",
        { TotalDays: < 1 } => $"vor {(int)ago.TotalHours} Std.",
        _ => $"vor {(int)ago.TotalDays} Tagen",
    };

    private static string SinceRu(TimeSpan ago) => ago switch
    {
        { TotalMinutes: < 1 } => "только что",
        { TotalHours: < 1 } => $"{(int)ago.TotalMinutes} мин. назад",
        { TotalDays: < 1 } => $"{(int)ago.TotalHours} ч. назад",
        _ => $"{(int)ago.TotalDays} {AppText.PluralRu((long)ago.TotalDays, "день", "дня", "дней")} назад",
    };

    private static int Weight(SyncStatus status) => status switch
    {
        SyncStatus.Refused => 4,
        SyncStatus.Outdated => 3,
        SyncStatus.Failed => 2,
        SyncStatus.Syncing => 1,
        _ => 0,
    };
}
