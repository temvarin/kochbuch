using Koch.Contracts;
using Koch.Media.Uploads;

namespace Koch.Media.Tests;

internal sealed class MemoryBlobs : IPhotoBlobs
{
    public Dictionary<Guid, byte[]> Uploads { get; } = [];
    public Dictionary<string, byte[]> Renditions { get; } = [];
    public List<string> Log { get; } = [];

    public Task<byte[]?> ReadUploadAsync(Guid id, CancellationToken cancellationToken) => Task.FromResult(Uploads.GetValueOrDefault(id));

    public Task DeleteUploadAsync(Guid id, CancellationToken cancellationToken)
    {
        Log.Add("delete");
        Uploads.Remove(id);
        return Task.CompletedTask;
    }

    public Task<bool> HasRenditionsAsync(Guid id, CancellationToken cancellationToken) =>
        Task.FromResult(Renditions.ContainsKey(PhotoPaths.Rendition(id, PhotoPaths.Widths[^1])));

    public Task CreateRenditionAsync(Guid id, Rendition rendition, CancellationToken cancellationToken)
    {
        Log.Add($"write {rendition.Width}");
        Renditions.TryAdd(PhotoPaths.Rendition(id, rendition.Width), rendition.Webp);
        return Task.CompletedTask;
    }
}
