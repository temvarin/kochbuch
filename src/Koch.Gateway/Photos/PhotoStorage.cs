using Azure;
using Azure.Identity;
using Azure.Storage.Blobs;
using Azure.Storage.Sas;
using Koch.Contracts;

namespace Koch.Gateway.Photos;

/// <summary>The gateway's two uses of Media's blobs: make room for an upload, read a rendition (<see cref="PhotoPaths"/>).</summary>
public interface IPhotoStorage
{
    /// <summary>Where uploads go, for the Content Security Policy's connect-src.</summary>
    Uri BlobEndpoint { get; }

    Task<UploadSlot> NewUploadSlotAsync(CancellationToken cancellationToken);

    /// <returns>null if Media hasn't written it (yet).</returns>
    Task<Stream?> OpenRenditionAsync(Guid photo, int width, CancellationToken cancellationToken);
}

/// <summary>
/// <see cref="IPhotoStorage"/> in Blob Storage with the gateway's managed identity. Upload links are user delegation SAS:
/// signed with a key Entra hands the identity, so no account key exists anywhere, and they can't grant more than the
/// identity has: create in <c>uploads/</c> (infra/modules/photos.bicep).
/// </summary>
public sealed class BlobPhotoStorage(BlobServiceClient service, TimeProvider clock) : IPhotoStorage
{
    /// <summary>Long enough for a slow upload of a large photo on mobile data, short enough to be useless afterwards.</summary>
    public static readonly TimeSpan SlotLifetime = TimeSpan.FromMinutes(10);

    public Uri BlobEndpoint => new(service.Uri.GetLeftPart(UriPartial.Authority));

    public async Task<UploadSlot> NewUploadSlotAsync(CancellationToken cancellationToken)
    {
        var photo = Guid.NewGuid();
        var blob = service.GetBlobContainerClient(PhotoPaths.Uploads).GetBlobClient(PhotoPaths.Upload(photo));
        var now = clock.GetUtcNow();
        var expires = now + SlotLifetime;
        var sas = new BlobSasBuilder
        {
            BlobContainerName = PhotoPaths.Uploads,
            BlobName = blob.Name,
            Resource = "b",
            StartsOn = now.AddMinutes(-5), // the phone's clock may be a little ahead of Azure's
            ExpiresOn = expires,
            Protocol = blob.Uri.Scheme == "https" ? SasProtocol.Https : SasProtocol.HttpsAndHttp,
        };
        sas.SetPermissions(BlobSasPermissions.Create | BlobSasPermissions.Write);

        Uri url;
        if (blob.CanGenerateSasUri)
        {
            url = blob.GenerateSasUri(sas); // only with an account key: the local emulator
        }
        else
        {
            var key = await service.GetUserDelegationKeyAsync(sas.StartsOn, expires, cancellationToken);
            url = new BlobUriBuilder(blob.Uri) { Sas = sas.ToSasQueryParameters(key.Value, service.AccountName) }.ToUri();
        }
        return new UploadSlot(photo, url, expires, PhotoPaths.MaxUploadBytes);
    }

    public async Task<Stream?> OpenRenditionAsync(Guid photo, int width, CancellationToken cancellationToken)
    {
        try
        {
            var blob = service.GetBlobContainerClient(PhotoPaths.Photos).GetBlobClient(PhotoPaths.Rendition(photo, width));
            return await blob.OpenReadAsync(cancellationToken: cancellationToken);
        }
        catch (RequestFailedException e) when (e.Status == 404)
        {
            return null;
        }
    }
}

public static class PhotoStorageRegistration
{
    /// <summary>
    /// <c>Photos:ConnectionString</c> (the local emulator, Azurite) or <c>Photos:BlobServiceUri</c> with the managed
    /// identity in <c>AZURE_CLIENT_ID</c>. Built when first needed, so the gateway starts without photo settings.
    /// </summary>
    public static IServiceCollection AddPhotoStorage(this IServiceCollection services) => services
        .AddSingleton<IPhotoStorage>(provider =>
        {
            var configuration = provider.GetRequiredService<IConfiguration>();
            var client = configuration["Photos:ConnectionString"] is { Length: > 0 } local
                ? new BlobServiceClient(local)
                : new BlobServiceClient(
                    new Uri(configuration["Photos:BlobServiceUri"] ?? throw new InvalidOperationException("The setting Photos:BlobServiceUri is missing.")),
                    new DefaultAzureCredential(new DefaultAzureCredentialOptions { ManagedIdentityClientId = configuration["AZURE_CLIENT_ID"] }));
            return new BlobPhotoStorage(client, provider.GetRequiredService<TimeProvider>());
        });
}
