using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Koch.Contracts;

namespace Koch.Media.Uploads;

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
            if (properties.Value.ContentLength > PhotoPaths.MaxUploadBytes) return new byte[PhotoPaths.MaxUploadBytes + 1];
            var content = await blob.DownloadContentAsync(new BlobDownloadOptions
            {
                Conditions = new BlobRequestConditions { IfMatch = properties.Value.ETag },
            }, cancellationToken);
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

    public async Task CreateRenditionAsync(Guid id, Rendition rendition, CancellationToken cancellationToken)
    {
        try
        {
            await Photos.GetBlobClient(PhotoPaths.Rendition(id, rendition.Width)).UploadAsync(new BinaryData(rendition.Webp),
                new BlobUploadOptions
                {
                    HttpHeaders = new BlobHttpHeaders { ContentType = "image/webp" },
                    Conditions = new BlobRequestConditions { IfNoneMatch = ETag.All },
                }, cancellationToken);
        }
        catch (RequestFailedException e) when (e.Status is 409 or 412)
        {
            // Already created by an earlier delivery.
        }
    }
}
