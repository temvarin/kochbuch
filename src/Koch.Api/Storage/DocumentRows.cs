using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;

namespace Koch.Api.Storage;

public sealed record StoredRow(string Id, string Json);

public sealed record RowWrite(string Id, string? Json, Func<string, string>? MergeWith = null);

public sealed record DocumentEntry(string Json, int Version);

public interface IDocumentRows
{
    Task<bool> CanReadAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<StoredRow>> WriteAndReadAsync(IReadOnlyList<RowWrite> writes, CancellationToken cancellationToken = default);
}

[JsonSourceGenerationOptions(JsonSerializerDefaults.Web,
    RespectNullableAnnotations = true, RespectRequiredConstructorParameters = true)]
[JsonSerializable(typeof(Dictionary<string, DocumentEntry>))]
internal sealed partial class DocumentMapJson : JsonSerializerContext;

public static class DocumentRows
{
    public const string Tombstone = """{"removed":true}""";

    public static string? Live(string? json) => json is null or Tombstone ? null : json;

    public static string Target(RowWrite write, string? before) =>
        write.Json is null ? Tombstone
        : write.MergeWith is { } merge && Live(before) is { } current ? merge(current)
        : write.Json;

    internal static (Dictionary<string, DocumentEntry> Next, bool Changed) Apply(
        IReadOnlyDictionary<string, DocumentEntry> current, IReadOnlyList<RowWrite> writes)
    {
        var next = new Dictionary<string, DocumentEntry>(current, StringComparer.Ordinal);
        var changed = false;
        foreach (var write in writes)
        {
            var hasRow = next.TryGetValue(write.Id, out var prev);
            var json = Target(write, hasRow ? prev!.Json : null);
            using var _ = JsonDocument.Parse(json);
            if (hasRow && string.Equals(prev!.Json, json, StringComparison.Ordinal)) continue;

            next[write.Id] = new DocumentEntry(json, hasRow ? prev!.Version + 1 : 1);
            changed = true;
        }
        return (next, changed);
    }

    internal static IReadOnlyList<StoredRow> LiveRows(IReadOnlyDictionary<string, DocumentEntry> map) =>
        [.. map.Where(kv => kv.Value.Json != Tombstone)
            .OrderBy(kv => kv.Key, StringComparer.Ordinal)
            .Select(kv => new StoredRow(kv.Key, kv.Value.Json))];
}

public sealed partial class BlobDocumentRows(BlobContainerClient container, string blobName, ILogger<BlobDocumentRows> logger) : IDocumentRows
{
    private const int MaxAttempts = 5;
    private static readonly BlobHttpHeaders JsonHeaders = new() { ContentType = "application/json; charset=utf-8" };

    public async Task<bool> CanReadAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await container.GetPropertiesAsync(cancellationToken: cancellationToken);
            return true;
        }
        catch (RequestFailedException e)
        {
            StoreUnreadable(logger, e);
            return false;
        }
    }

    public async Task<IReadOnlyList<StoredRow>> WriteAndReadAsync(
        IReadOnlyList<RowWrite> writes, CancellationToken cancellationToken = default)
    {
        var blob = container.GetBlobClient(blobName);
        for (var attempt = 0; attempt < MaxAttempts; attempt++)
        {
            var (current, etag) = await ReadMapAsync(blob, cancellationToken);
            var (next, changed) = DocumentRows.Apply(current, writes);
            if (!changed) return DocumentRows.LiveRows(next);

            var options = new BlobUploadOptions
            {
                HttpHeaders = JsonHeaders,
                Conditions = etag is null
                    ? new BlobRequestConditions { IfNoneMatch = ETag.All }
                    : new BlobRequestConditions { IfMatch = etag.Value },
            };
            try
            {
                await blob.UploadAsync(BinaryData.FromString(JsonSerializer.Serialize(next, DocumentMapJson.Default.DictionaryStringDocumentEntry)),
                    options, cancellationToken);
                return DocumentRows.LiveRows(next);
            }
            catch (RequestFailedException e) when (e.Status is 409 or 412)
            {
                // Another writer updated the blob concurrently; re-read and retry.
            }
        }
        throw new InvalidOperationException($"Concurrent writes to '{blobName}' exceeded {MaxAttempts} attempts.");
    }

    internal async Task<int> VersionOfAsync(string id, CancellationToken cancellationToken = default)
    {
        var (current, _) = await ReadMapAsync(container.GetBlobClient(blobName), cancellationToken);
        return current.TryGetValue(id, out var entry) ? entry.Version : 0;
    }

    private static async Task<(Dictionary<string, DocumentEntry> Map, ETag? ETag)> ReadMapAsync(
        BlobClient blob, CancellationToken cancellationToken)
    {
        try
        {
            var result = await blob.DownloadContentAsync(cancellationToken);
            var map = JsonSerializer.Deserialize(result.Value.Content.ToString(), DocumentMapJson.Default.DictionaryStringDocumentEntry)
                ?? throw new JsonException($"Stored blob '{blob.Name}' deserialized to null.");
            return (new Dictionary<string, DocumentEntry>(map, StringComparer.Ordinal), result.Value.Details.ETag);
        }
        catch (RequestFailedException e) when (e.Status == 404)
        {
            return ([], null);
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "The service's blob container can't be read.")]
    private static partial void StoreUnreadable(ILogger logger, Exception exception);
}

public sealed class FileDocumentRows(string filePath) : IDocumentRows
{
    private readonly Lock gate = new();

    public Task<bool> CanReadAsync(CancellationToken cancellationToken = default) => Task.FromResult(true);

    public Task<IReadOnlyList<StoredRow>> WriteAndReadAsync(
        IReadOnlyList<RowWrite> writes, CancellationToken cancellationToken = default)
    {
        lock (gate)
        {
            var current = ReadMap();
            var (next, changed) = DocumentRows.Apply(current, writes);
            if (changed)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
                var temp = $"{filePath}.{Guid.NewGuid():N}.tmp";
                File.WriteAllText(temp, JsonSerializer.Serialize(next, DocumentMapJson.Default.DictionaryStringDocumentEntry), Encoding.UTF8);
                File.Move(temp, filePath, overwrite: true);
            }
            return Task.FromResult(DocumentRows.LiveRows(next));
        }
    }

    internal int VersionOf(string id)
    {
        lock (gate) return ReadMap().TryGetValue(id, out var entry) ? entry.Version : 0;
    }

    private Dictionary<string, DocumentEntry> ReadMap()
    {
        if (!File.Exists(filePath)) return new(StringComparer.Ordinal);
        var map = JsonSerializer.Deserialize(File.ReadAllText(filePath, Encoding.UTF8), DocumentMapJson.Default.DictionaryStringDocumentEntry)
            ?? throw new JsonException($"Stored file '{filePath}' deserialized to null.");
        return new(map, StringComparer.Ordinal);
    }
}
