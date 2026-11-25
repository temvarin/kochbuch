using System.Net;
using System.Text.Json;
using Koch.App.Account;
using Koch.App.Shell;
using Koch.Domain.Kernel;

namespace Koch.App.Sync;

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
                    // The retry's timer starts before anyone hears of the failure, so whoever sees Failed also finds
                    // the retry already due at RetryAfter (a test's clock advanced on Failed included).
                    var retry = WaitToRetryAsync();
                    Set(SyncStatus.Failed);
                    await retry;
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
            using var response = await http.PostAsync(new Uri(channel.Path, UriKind.Relative), channel.Request(pending.Changes, state.Data.State?.Base));
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
