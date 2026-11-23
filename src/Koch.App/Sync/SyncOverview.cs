using Koch.App.Account;

namespace Koch.App.Sync;

/// <summary>
/// Both syncs as the user sees them: one sign-in check at start, one status (the worst of the two), one button. Each
/// service is synced on its own, so one of them being down doesn't hold up the other.
/// </summary>
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

    /// <summary>Refused and outdated before failed before syncing: what the user must act on comes first.</summary>
    public SyncStatus Status => new[] { recipes.Status, shopping.Status }.OrderByDescending(Weight).First();

    public bool CanSync => recipes.CanSync || shopping.CanSync;

    public bool HasUnsyncedChanges => recipes.HasUnsyncedChanges || shopping.HasUnsyncedChanges;

    /// <summary>The older of the two; null if either never synced.</summary>
    public DateTimeOffset? LastSync => recipes.LastSync is { } a && shopping.LastSync is { } b ? (a < b ? a : b) : null;

    public async Task StartAsync()
    {
        await account.CheckAsync();
        await Task.WhenAll(recipes.StartAsync(), shopping.StartAsync());
    }

    public async Task ResumeAsync()
    {
        if (account.Status is AccountStatus.Unreachable or AccountStatus.Unknown) await account.CheckAsync(); // opened offline
        await Task.WhenAll(recipes.ResumeAsync(), shopping.ResumeAsync());
    }

    public Task SyncNowAsync() => Task.WhenAll(recipes.SyncNowAsync(), shopping.SyncNowAsync());

    /// <summary>The state in a few German words: "Abgeglichen · vor 2 Min.", "Änderungen warten", …</summary>
    public string Describe(DateTimeOffset now) => Status switch
    {
        SyncStatus.Syncing => "Abgleich läuft …",
        SyncStatus.Failed => "Abgleich fehlgeschlagen, neuer Versuch gleich",
        SyncStatus.Outdated => "Neue Version – bitte neu laden",
        SyncStatus.Refused => "Abgleich abgelehnt – bitte neu laden",
        _ when HasUnsyncedChanges => "Änderungen warten auf den Abgleich",
        _ when LastSync is { } last => $"Abgeglichen · {Since(now - last)}",
        _ => "Noch nie abgeglichen",
    };

    private static string Since(TimeSpan ago) => ago switch
    {
        { TotalMinutes: < 1 } => "gerade eben",
        { TotalHours: < 1 } => $"vor {(int)ago.TotalMinutes} Min.",
        { TotalDays: < 1 } => $"vor {(int)ago.TotalHours} Std.",
        _ => $"vor {(int)ago.TotalDays} Tagen",
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
