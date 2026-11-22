using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Koch.App.Account;
using Koch.App.Shell;
using Koch.Kernel;
using Koch.Recipes.Contracts;
using Koch.Recipes.Domain.Sync;
using Koch.Recipes.Domain.Wire;
using Koch.Shopping.Contracts;
using Koch.Shopping.Domain;

namespace Koch.App.Sync;

public enum SyncStatus
{
    Ready,
    Syncing,
    /// <summary>The changes stay here and go with the next sync, which is tried once after <see cref="SyncTiming.RetryAfter"/>.</summary>
    Failed,
    /// <summary>The service runs a newer protocol; this app has to be reloaded.</summary>
    Outdated,
    /// <summary>The service refused the changes (400). They stay here; no sync runs until the app is restarted.</summary>
    Refused,
}

public static class SyncTiming
{
    /// <summary>The wait after a change, so that a few quick changes go out together.</summary>
    public static readonly TimeSpan AfterSave = TimeSpan.FromSeconds(2);

    /// <summary>Coming back within this time of the last sync, e.g. after switching apps, doesn't sync again.</summary>
    public static readonly TimeSpan Recent = TimeSpan.FromMinutes(1);

    /// <summary>A failed sync is tried once more after this, and no more often, so a struggling service isn't flooded.</summary>
    public static readonly TimeSpan RetryAfter = TimeSpan.FromSeconds(30);
}

/// <summary>How a book travels to its service: the path behind the gateway and the JSON both ways.</summary>
public abstract class SyncChannel<TBook, TChanges>
    where TBook : class, ISyncBook<TBook, TChanges>
    where TChanges : class, IBookChanges<TBook, TChanges>
{
    /// <summary>Relative to the app, e.g. <c>api/recipes/sync</c>.</summary>
    public abstract string Path { get; }

    /// <param name="data">The book and its sync state, for what the request needs besides the changes.</param>
    public abstract HttpContent Request(TChanges changes, Synced<TBook, TChanges> data);

    /// <exception cref="JsonException">No answer, or one that breaks the domain's rules: it counts as no answer.</exception>
    public abstract Task<TBook> ReadAnswerAsync(HttpContent content);
}

/// <summary>
/// Syncs one book with its service: shortly after each change, when the app comes back to the front, and on demand,
/// whenever the account allows it. Only one sync runs at a time, so answers can't arrive out of order and bring back
/// older data. Ported from kassensturz's SyncService; <see cref="SyncOverview"/> starts and shows them together.
/// </summary>
public abstract class SyncService<TBook, TChanges>
    where TBook : class, ISyncBook<TBook, TChanges>
    where TChanges : class, IBookChanges<TBook, TChanges>
{
    private readonly DataState<TBook, TChanges> state;
    private readonly AccountService account;
    private readonly HttpClient http;
    private readonly TimeProvider clock;
    private readonly SyncChannel<TBook, TChanges> channel;
    private bool started, running, again, scheduled;
    private TaskCompletionSource? retryNow;

    protected SyncService(DataState<TBook, TChanges> state, AccountService account, HttpClient http, TimeProvider clock,
        SyncChannel<TBook, TChanges> channel)
    {
        (this.state, this.account, this.http, this.clock, this.channel) = (state, account, http, clock, channel);
        account.Changed += () => Changed?.Invoke(); // CanSync depends on the account
    }

    public SyncStatus Status { get; private set; }

    public event Action? Changed;

    public bool CanSync => account.Status == AccountStatus.SignedIn && Status is not (SyncStatus.Outdated or SyncStatus.Refused);

    /// <summary>null if never, which also means the data never left this phone.</summary>
    public DateTimeOffset? LastSync => state.Data.State?.LastSync;

    public bool HasUnsyncedChanges => state.Data.HasUnsyncedChanges;

    /// <summary>Later calls do nothing. The account must have been checked before (<see cref="SyncOverview.StartAsync"/>).</summary>
    public async Task StartAsync()
    {
        if (started) return;
        started = true;
        state.Changed += OnDataChanged;
        await SyncAsync();
    }

    /// <summary>When the app comes back to the front: phones rarely restart an installed app, they bring it back.</summary>
    public async Task ResumeAsync()
    {
        if (!started || running) return;
        if (LastSync is { } last && clock.GetUtcNow() - last < SyncTiming.Recent) return;
        await SyncAsync();
    }

    /// <summary>"Jetzt abgleichen". After a failed sync, it ends the wait for the second try.</summary>
    public Task SyncNowAsync()
    {
        if (retryNow is not { } now) return SyncAsync();
        now.TrySetResult();
        return Task.CompletedTask;
    }

    private void OnDataChanged()
    {
        if (!CanSync || !state.Data.HasUnsyncedChanges) return;
        if (running) again = true; // sent as soon as the running sync is done
        else if (!scheduled) _ = SyncSoonAsync();
    }

    private async Task SyncSoonAsync()
    {
        scheduled = true;
        await Task.Delay(SyncTiming.AfterSave, clock);
        scheduled = false;
        await SyncAsync();
    }

    private async Task SyncAsync()
    {
        if (!CanSync) return;
        if (running)
        {
            again = true;
            return;
        }
        running = true;
        try
        {
            do
            {
                again = false;
                Set(SyncStatus.Syncing);
                var result = await SendAsync();
                if (result == SyncStatus.Failed && CanSync)
                {
                    Set(SyncStatus.Failed);
                    await WaitToRetryAsync();
                    if (!CanSync) break;
                    Set(SyncStatus.Syncing);
                    result = await SendAsync();
                }
                Set(result);
            } while (again && CanSync && Status == SyncStatus.Ready && state.Data.HasUnsyncedChanges);
        }
        finally
        {
            running = false;
        }
    }

    private async Task WaitToRetryAsync()
    {
        retryNow = new TaskCompletionSource();
        await Task.WhenAny(Task.Delay(SyncTiming.RetryAfter, clock), retryNow.Task);
        retryNow = null;
    }

    /// <returns>Also <see cref="SyncStatus.Ready"/> when refused because of the account, which AccountService shows.</returns>
    private async Task<SyncStatus> SendAsync()
    {
        var pending = state.Data.StartSync();
        try
        {
            using var response = await http.PostAsync(new Uri(channel.Path, UriKind.Relative), channel.Request(pending.Changes, state.Data));
            switch (response.StatusCode)
            {
                case HttpStatusCode.Unauthorized: // the sign-in has expired
                    account.SignedOut();
                    return SyncStatus.Ready;
                case HttpStatusCode.Forbidden:
                    account.NotMember();
                    return SyncStatus.Ready;
                case HttpStatusCode.Conflict:
                    return SyncStatus.Outdated;
                case HttpStatusCode.BadRequest:
                    return SyncStatus.Refused;
            }
            response.EnsureSuccessStatusCode();
            var answer = await channel.ReadAnswerAsync(response.Content);
            state.Change(data => data.CompleteSync(pending, answer, clock.GetUtcNow()));
        }
        catch (Exception e) when (e is HttpRequestException or JsonException or TaskCanceledException)
        {
            // The service may have saved the changes anyway, and the next sync allows for that (Synced.Unconfirmed).
            state.Change(data => data.SyncFailed(pending));
            return SyncStatus.Failed;
        }
        return SyncStatus.Ready; // changes made meanwhile already asked for another sync through OnDataChanged
    }

    private void Set(SyncStatus status)
    {
        Status = status;
        Changed?.Invoke();
    }
}

/// <summary>The recipe book with the Recipes service.</summary>
public sealed class RecipeSync(RecipeState state, AccountService account, HttpClient http, TimeProvider clock)
    : SyncService<RecipeBook, BookChanges>(state, account, http, clock, new Channel())
{
    private sealed class Channel : SyncChannel<RecipeBook, BookChanges>
    {
        public override string Path => "api/recipes/sync";

        // With the bases, so the service can merge with what the other phone changed meanwhile (docs/adr/0012).
        public override HttpContent Request(BookChanges changes, Synced<RecipeBook, BookChanges> data) =>
            JsonContent.Create(changes.ToJson(((SyncedBook)data).BasesFor(changes)), RecipesJson.Default.SyncRequestJson);

        public override async Task<RecipeBook> ReadAnswerAsync(HttpContent content)
        {
            var json = await content.ReadFromJsonAsync(RecipesJson.Default.SyncResponseJson) ?? throw new JsonException("No answer.");
            try
            {
                return json.ToDomain();
            }
            catch (ArgumentException e)
            {
                throw new JsonException("The answer breaks the domain's rules.", e);
            }
        }
    }
}

/// <summary>The shopping lists with the Shopping service.</summary>
public sealed class ShoppingSync(ShoppingState state, AccountService account, HttpClient http, TimeProvider clock)
    : SyncService<ShoppingBook, ShoppingChanges>(state, account, http, clock, new Channel())
{
    private sealed class Channel : SyncChannel<ShoppingBook, ShoppingChanges>
    {
        public override string Path => "api/shopping/sync";

        public override HttpContent Request(ShoppingChanges changes, Synced<ShoppingBook, ShoppingChanges> data) =>
            JsonContent.Create(changes.ToJson(), ShoppingJson.Default.ShoppingSyncRequestJson);

        public override async Task<ShoppingBook> ReadAnswerAsync(HttpContent content)
        {
            var json = await content.ReadFromJsonAsync(ShoppingJson.Default.ShoppingSyncResponseJson) ?? throw new JsonException("No answer.");
            try
            {
                return json.ToDomain();
            }
            catch (ArgumentException e)
            {
                throw new JsonException("The answer breaks the domain's rules.", e);
            }
        }
    }
}

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

    private static int Weight(SyncStatus status) => status switch
    {
        SyncStatus.Refused => 4,
        SyncStatus.Outdated => 3,
        SyncStatus.Failed => 2,
        SyncStatus.Syncing => 1,
        _ => 0,
    };
}
