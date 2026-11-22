namespace Koch.Kernel;

/// <summary>
/// A set of items a service syncs as a whole: the recipe book, the shopping lists. Immutable; every change gives a new
/// one. Implemented by each domain, so <see cref="Synced{TBook, TChanges}"/> works for all of them.
/// </summary>
public interface ISyncBook<TBook, TChanges>
    where TBook : ISyncBook<TBook, TChanges>
    where TChanges : IBookChanges<TBook, TChanges>
{
    static abstract TBook Empty { get; }

    /// <exception cref="ArgumentException">The result breaks a rule of the book.</exception>
    TBook Apply(TChanges changes);
}

/// <summary>What changed in a <typeparamref name="TBook"/>: one <see cref="ItemChanges{T}"/> per kind of item.</summary>
public interface IBookChanges<TBook, TChanges>
    where TBook : ISyncBook<TBook, TChanges>
    where TChanges : IBookChanges<TBook, TChanges>
{
    static abstract TChanges Between(TBook before, TBook after);

    bool IsEmpty { get; }

    /// <summary>For an item in both, this one's version.</summary>
    TChanges Union(TChanges other);

    /// <summary>The same items as they are in <paramref name="book"/> now.</summary>
    TChanges Now(TBook book);
}

/// <summary>
/// The last sync. The book as the service confirmed it is the base, and the next sync sends what changed since, much
/// like a git branch and the commit it started from.
/// </summary>
public sealed record SyncState<TBook>(DateTimeOffset LastSync, TBook Base);

/// <summary>A sync under way: what was sent, and the book as it was when it was sent.</summary>
public sealed record PendingSync<TBook, TChanges>(TChanges Changes, TBook Sent);

/// <summary>
/// A book on a phone and what the phone knows about its sync with the service. Ported from kassensturz's SyncedData
/// (docs/adr/0004). Every phone is a leader that may be offline for days, so it keeps the last confirmed book as the
/// base of the next sync (DDIA ch06 p.220-221).
/// </summary>
public class Synced<TBook, TChanges>
    where TBook : class, ISyncBook<TBook, TChanges>
    where TChanges : class, IBookChanges<TBook, TChanges>
{
    public Synced()
    {
    }

    public Synced(TBook book, SyncState<TBook>? state, TChanges? unconfirmed) =>
        (Book, State, Unconfirmed) = (book, state, unconfirmed);

    public TBook Book { get; private set; } = TBook.Empty;

    /// <summary>null until the first sync.</summary>
    public SyncState<TBook>? State { get; private set; }

    /// <summary>
    /// Changes of syncs whose answer never came, so the service may or may not have saved them. Until a sync succeeds,
    /// the items they touched are sent again as they are now. A diff against the base alone would miss an item that a
    /// lost sync added on the service and that was removed here afterwards.
    /// </summary>
    public TChanges? Unconfirmed { get; private set; }

    /// <summary>Before the first sync, everything counts as unsynced.</summary>
    public bool HasUnsyncedChanges => !Changes().IsEmpty;

    /// <summary>A change made on this phone.</summary>
    public void Change(Func<TBook, TBook> change) => Book = change(Book);

    public PendingSync<TBook, TChanges> StartSync() => new(Changes(), Book);

    public void CompleteSync(PendingSync<TBook, TChanges> sent, TBook answer, DateTimeOffset now)
    {
        // The answer lacks what changed here while the request was under way. That goes on top of it again, like a git
        // rebase, and waits for the next sync.
        Book = answer.Apply(TChanges.Between(sent.Sent, Book));
        State = new SyncState<TBook>(now, answer);
        Unconfirmed = null;
    }

    public void SyncFailed(PendingSync<TBook, TChanges> sent) => Unconfirmed = Unconfirmed?.Union(sent.Changes) ?? sent.Changes;

    private TChanges Changes()
    {
        var changes = TChanges.Between(State?.Base ?? TBook.Empty, Book);
        return Unconfirmed is null ? changes : changes.Union(Unconfirmed.Now(Book));
    }
}
