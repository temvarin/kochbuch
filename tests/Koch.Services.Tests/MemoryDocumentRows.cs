using Koch.Api.Storage;

namespace Koch.Services.Tests;

public sealed class MemoryDocumentRows : IDocumentRows
{
    private readonly Dictionary<string, (string Json, int Version)> rows = [];
    private readonly Lock gate = new();

    public int VersionOf(string id)
    {
        lock (gate) return rows.TryGetValue(id, out var row) ? row.Version : 0;
    }

    public Task<bool> CanReadAsync(CancellationToken cancellationToken = default) => Task.FromResult(true);

    public Task<IReadOnlyList<StoredRow>> WriteAndReadAsync(IReadOnlyList<RowWrite> writes, CancellationToken cancellationToken = default)
    {
        lock (gate)
        {
            var newRows = new Dictionary<string, (string Json, int Version)>(rows);
            foreach (var write in writes)
            {
                var before = newRows.TryGetValue(write.Id, out var row) ? row.Json : null;
                var json = DocumentRows.Target(write, before);
                if (before == json) continue;
                newRows[write.Id] = (json, before is null ? 1 : row.Version + 1);
            }

            rows.Clear();
            foreach (var (id, row) in newRows) rows[id] = row;
            IReadOnlyList<StoredRow> live = [.. rows.Where(r => r.Value.Json != DocumentRows.Tombstone)
                .OrderBy(r => r.Key, StringComparer.Ordinal).Select(r => new StoredRow(r.Key, r.Value.Json))];
            return Task.FromResult(live);
        }
    }
}
