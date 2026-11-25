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
