namespace Koch.Domain.Kernel;

public interface ISyncBook<TBook, TChanges>
    where TBook : ISyncBook<TBook, TChanges>
    where TChanges : IBookChanges<TBook, TChanges>
{
    static abstract TBook Empty { get; }

    /// <exception cref="ArgumentException">The result breaks a rule of the book.</exception>
    TBook Apply(TChanges changes);
}

public interface IBookChanges<TBook, TChanges>
    where TBook : ISyncBook<TBook, TChanges>
    where TChanges : IBookChanges<TBook, TChanges>
{
    static abstract TChanges Between(TBook before, TBook after);

    bool IsEmpty { get; }

    TChanges Union(TChanges other);

    TChanges Now(TBook book);
}

public sealed record SyncState<TBook>(DateTimeOffset LastSync, TBook Base);

public sealed record PendingSync<TBook, TChanges>(TChanges Changes, TBook Sent);

/// <summary>
/// A book on a phone and what the phone knows about its sync with the server. Every phone is a leader that may be
/// offline for days, so it keeps the last confirmed book as the base of the next sync.
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

    public SyncState<TBook>? State { get; private set; }

    public TChanges? Unconfirmed { get; private set; }

    public bool HasUnsyncedChanges => !Changes().IsEmpty;

    public void Change(Func<TBook, TBook> change) => Book = change(Book);

    public PendingSync<TBook, TChanges> StartSync() => new(Changes(), Book);

    public void CompleteSync(PendingSync<TBook, TChanges> sent, TBook answer, DateTimeOffset now)
    {
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
