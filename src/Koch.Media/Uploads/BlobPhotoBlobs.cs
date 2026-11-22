using System.Runtime.CompilerServices;
using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Koch.Media.Contracts;
using Koch.Media.Imaging;
using Koch.Media.Lifecycle;

namespace Koch.Media.Uploads;

/// <summary><see cref="IPhotoBlobs"/> in Blob Storage, reached with Media's managed identity.</summary>
public sealed class BlobPhotoBlobs(BlobServiceClient service) : IPhotoBlobs
{
    private BlobContainerClient Uploads => service.GetBlobContainerClient(PhotoPaths.Uploads);
    private BlobContainerClient Photos => service.GetBlobContainerClient(PhotoPaths.Photos);

    public async Task<byte[]?> ReadUploadAsync(Guid id, CancellationToken cancellationToken)
    {
        try
        {
            var blob = Uploads.GetBlobClient(PhotoPaths.Upload(id));
            var properties = await blob.GetPropertiesAsync(cancellationToken: cancellationToken);
            // Checked before downloading: a SAS can't limit the size of an upload.
            if (properties.Value.ContentLength > PhotoPaths.MaxUploadBytes) return new byte[PhotoPaths.MaxUploadBytes + 1];
            var content = await blob.DownloadContentAsync(cancellationToken);
            return content.Value.Content.ToArray();
        }
        catch (RequestFailedException e) when (e.Status == 404)
        {
            return null;
        }
    }

    public Task DeleteUploadAsync(Guid id, CancellationToken cancellationToken) =>
        Uploads.GetBlobClient(PhotoPaths.Upload(id)).DeleteIfExistsAsync(cancellationToken: cancellationToken);

    public async Task<bool> HasRenditionsAsync(Guid id, CancellationToken cancellationToken) =>
        await Photos.GetBlobClient(PhotoPaths.Rendition(id, PhotoPaths.Widths[^1])).ExistsAsync(cancellationToken);

    public async Task CreateRenditionAsync(Guid id, Rendition rendition, PhotoUsage usage, CancellationToken cancellationToken)
    {
        try
        {
            await Photos.GetBlobClient(PhotoPaths.Rendition(id, rendition.Width)).UploadAsync(new BinaryData(rendition.Webp),
                new BlobUploadOptions
                {
                    HttpHeaders = new BlobHttpHeaders { ContentType = "image/webp" },
                    Tags = usage.ToTags(),
                    // Create only: an existing rendition has the same bytes and maybe newer tags.
                    Conditions = new BlobRequestConditions { IfNoneMatch = ETag.All },
                }, cancellationToken);
        }
        catch (RequestFailedException e) when (e.Status is 409 or 412)
        {
            // Already there: made by an earlier delivery, or by a photo event that came first.
        }
    }

    public async Task<TagOutcome> TagRenditionAsync(Guid id, int width, PhotoUsage usage, CancellationToken cancellationToken)
    {
        try
        {
            // Blob Storage checks the condition and writes in one step, like a compare-and-set (DDIA ch9 p.376).
            await Photos.GetBlobClient(PhotoPaths.Rendition(id, width)).SetTagsAsync(usage.ToTags(),
                new BlobRequestConditions { TagConditions = usage.IfNewer }, cancellationToken);
            return TagOutcome.Applied;
        }
        catch (RequestFailedException e) when (e.Status == 404)
        {
            return TagOutcome.Missing;
        }
        catch (RequestFailedException e) when (e.Status == 412)
        {
            return TagOutcome.Stale;
        }
    }

    public async IAsyncEnumerable<string> FindDetachedAsync(DateOnly before,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await foreach (var blob in Photos.FindBlobsByTagsAsync(PhotoUsage.DetachedBefore(before), cancellationToken))
            yield return blob.BlobName;
    }

    public async Task<bool> DeleteIfDetachedAsync(string name, DateOnly before, CancellationToken cancellationToken)
    {
        try
        {
            await Photos.GetBlobClient(name).DeleteAsync(DeleteSnapshotsOption.None,
                new BlobRequestConditions { TagConditions = PhotoUsage.DetachedBefore(before) }, cancellationToken);
            return true;
        }
        catch (RequestFailedException e) when (e.Status is 404 or 412)
        {
            return false;
        }
    }
}
