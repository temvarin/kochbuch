using Koch.ServiceDefaults.Documents;
using Koch.Telemetry;

namespace Koch.Testing;

/// <summary>
/// A service's rows and outbox in memory, for tests that run the real service without SQL Server. Passes the same
/// contract tests as <see cref="SqlDocumentRows"/> and <see cref="SqlOutbox"/> (Koch.Recipes.Tests, DocumentRowsContract),
/// so it behaves alike where it matters.
/// </summary>
public sealed class MemoryDocumentRows(RowEvents? events = null) : IDocumentRows, IOutbox
{
    private readonly Dictionary<string, (string Json, int Version)> rows = [];
    private readonly List<(PendingEvent Event, bool Sent)> outbox = [];
    private readonly Lock gate = new();
    private long sequence;

    /// <summary>How often each row was saved; unchanged writes don't count, as in SQL.</summary>
    public int VersionOf(string id)
    {
        lock (gate) return rows.TryGetValue(id, out var row) ? row.Version : 0;
    }

    /// <summary>Every event saved so far, sent or not.</summary>
    public IReadOnlyList<OutboxEvent> Events
    {
        get
        {
            lock (gate) return [.. outbox.Select(e => e.Event.Event)];
        }
    }

    public Task<bool> CanReadAsync(CancellationToken cancellationToken = default) => Task.FromResult(true);

    public Task<IReadOnlyList<StoredRow>> WriteAndReadAsync(IReadOnlyList<RowWrite> writes, CancellationToken cancellationToken = default)
    {
        lock (gate)
        {
            // Like the SQL transaction: everything is worked out first, and nothing is kept if anything throws.
            var newRows = new Dictionary<string, (string Json, int Version)>(rows);
            List<OutboxEvent> newEvents = [];
            foreach (var write in writes)
            {
                var before = newRows.TryGetValue(write.Id, out var row) ? row.Json : null;
                var json = DocumentRows.Target(write, before);
                if (before == json) continue;
                newRows[write.Id] = (json, before is null ? 1 : row.Version + 1);
                if (DocumentRows.Change(write.Id, before, json) is { } change && events is not null) newEvents.AddRange(events(change));
            }

            var traceParent = AppTelemetry.TraceParent();
            rows.Clear();
            foreach (var (id, row) in newRows) rows[id] = row;
            foreach (var e in newEvents) outbox.Add((new PendingEvent(++sequence, DateTimeOffset.UnixEpoch.AddSeconds(sequence), e, traceParent), false));
            IReadOnlyList<StoredRow> live = [.. rows.Where(r => r.Value.Json != DocumentRows.Tombstone)
                .OrderBy(r => r.Key, StringComparer.Ordinal).Select(r => new StoredRow(r.Key, r.Value.Json))];
            return Task.FromResult(live);
        }
    }

    public Task<IReadOnlyList<PendingEvent>> ReadUnsentAsync(int max, CancellationToken cancellationToken = default)
    {
        lock (gate)
        {
            IReadOnlyList<PendingEvent> unsent = [.. outbox.Where(e => !e.Sent).Select(e => e.Event).Take(max)];
            return Task.FromResult(unsent);
        }
    }

    public Task MarkSentAsync(IReadOnlyCollection<long> sequences, CancellationToken cancellationToken = default)
    {
        lock (gate)
        {
            for (var i = 0; i < outbox.Count; i++)
                if (sequences.Contains(outbox[i].Event.Sequence)) outbox[i] = (outbox[i].Event, true);
        }
        return Task.CompletedTask;
    }
}
