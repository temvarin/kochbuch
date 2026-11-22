using System.Runtime.CompilerServices;
using Koch.Media.Contracts;
using Koch.Media.Imaging;
using Koch.Media.Lifecycle;
using Koch.Media.Uploads;

namespace Koch.Media.Tests;

/// <summary>
/// Media's blobs in memory, with Blob Storage's rules where they matter: renditions are created only if missing, tags
/// change only for a higher sequence, and deleting checks the tags (BlobPhotoBlobs does this with conditional requests).
/// </summary>
internal sealed class MemoryBlobs : IPhotoBlobs
{
    public Dictionary<Guid, byte[]> Uploads { get; } = [];
    public Dictionary<string, (byte[] Webp, PhotoUsage Usage)> Renditions { get; } = [];
    public List<string> Log { get; } = [];

    /// <summary>Runs just before a delete is checked: something else changing the photo in between.</summary>
    public Action<string>? BeforeDelete { get; set; }

    public PhotoUsage UsageOf(Guid id, int width) => Renditions[PhotoPaths.Rendition(id, width)].Usage;

    public Task<byte[]?> ReadUploadAsync(Guid id, CancellationToken cancellationToken) => Task.FromResult(Uploads.GetValueOrDefault(id));

    public Task DeleteUploadAsync(Guid id, CancellationToken cancellationToken)
    {
        Log.Add("delete");
        Uploads.Remove(id);
        return Task.CompletedTask;
    }

    public Task<bool> HasRenditionsAsync(Guid id, CancellationToken cancellationToken) =>
        Task.FromResult(Renditions.ContainsKey(PhotoPaths.Rendition(id, PhotoPaths.Widths[^1])));

    public Task CreateRenditionAsync(Guid id, Rendition rendition, PhotoUsage usage, CancellationToken cancellationToken)
    {
        Log.Add($"write {rendition.Width}");
        Renditions.TryAdd(PhotoPaths.Rendition(id, rendition.Width), (rendition.Webp, usage));
        return Task.CompletedTask;
    }

    public Task<TagOutcome> TagRenditionAsync(Guid id, int width, PhotoUsage usage, CancellationToken cancellationToken)
    {
        var name = PhotoPaths.Rendition(id, width);
        if (!Renditions.TryGetValue(name, out var rendition)) return Task.FromResult(TagOutcome.Missing);
        if (string.CompareOrdinal(rendition.Usage.Sequence, usage.Sequence) >= 0) return Task.FromResult(TagOutcome.Stale);
        Renditions[name] = (rendition.Webp, usage);
        return Task.FromResult(TagOutcome.Applied);
    }

    public async IAsyncEnumerable<string> FindDetachedAsync(DateOnly before, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        foreach (var name in Renditions.Where(r => IsDetachedBefore(r.Value.Usage, before)).Select(r => r.Key).ToList())
        {
            await Task.Yield();
            yield return name;
        }
    }

    public Task<bool> DeleteIfDetachedAsync(string name, DateOnly before, CancellationToken cancellationToken)
    {
        BeforeDelete?.Invoke(name);
        var deleted = Renditions.TryGetValue(name, out var rendition) && IsDetachedBefore(rendition.Usage, before)
            && Renditions.Remove(name);
        return Task.FromResult(deleted);
    }

    private static bool IsDetachedBefore(PhotoUsage usage, DateOnly before) =>
        usage.State == PhotoUsage.Detached && usage.Since < before;
}
