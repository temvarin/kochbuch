using System.Text.Json;
using Koch.Domain.Kernel;

namespace Koch.App.Sync;

/// <summary>How a book travels to its service: the path behind the gateway and the JSON both ways.</summary>
public abstract class SyncChannel<TBook, TChanges>
    where TBook : class, ISyncBook<TBook, TChanges>
    where TChanges : class, IBookChanges<TBook, TChanges>
{
    /// <summary>Relative to the app, e.g. <c>api/recipes/sync</c>.</summary>
    public abstract string Path { get; }

    /// <param name="confirmed">The book as the service last confirmed it; null before the first sync.</param>
    public abstract HttpContent Request(TChanges changes, TBook? confirmed);

    /// <exception cref="JsonException">No answer, or one that breaks the domain's rules: it counts as no answer.</exception>
    public abstract Task<TBook> ReadAnswerAsync(HttpContent content);
}
