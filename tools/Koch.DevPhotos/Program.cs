using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Koch.Contracts;
using Koch.Media.Uploads;
using Microsoft.Extensions.Logging.Abstractions;

// Usage, with Azurite running (npx -y azurite@3 --location .azurite --skipApiVersionCheck):
//   dotnet run --project tools/Koch.DevPhotos -- [origin of the app, default http://localhost:8091]
const string Azurite = "UseDevelopmentStorage=true";
var origin = args.Length > 0 ? args[0] : "http://localhost:8091";

var service = new BlobServiceClient(Azurite);
foreach (var name in new[] { PhotoPaths.Uploads, PhotoPaths.Photos })
    await service.GetBlobContainerClient(name).CreateIfNotExistsAsync();

BlobServiceProperties properties = await service.GetPropertiesAsync();
properties.Cors =
[
    new BlobCorsRule
    {
        AllowedOrigins = origin,
        AllowedMethods = "PUT,OPTIONS",
        AllowedHeaders = "content-type,x-ms-blob-type,x-ms-version,x-ms-client-request-id",
        ExposedHeaders = "x-ms-request-id",
        MaxAgeInSeconds = 3600,
    },
];
await service.SetPropertiesAsync(properties);
Console.WriteLine($"Azurite ready for uploads from {origin}. Processing uploads; Ctrl+C stops.");

var processor = new PhotoProcessor(new BlobPhotoBlobs(service), NullLogger<PhotoProcessor>.Instance);
var uploads = service.GetBlobContainerClient(PhotoPaths.Uploads);
using var stop = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => (e.Cancel, _) = (true, stop.CancelAsync());
using var timer = new PeriodicTimer(TimeSpan.FromSeconds(2));
try
{
    do
    {
        await foreach (var blob in uploads.GetBlobsAsync(cancellationToken: stop.Token))
        {
            if (!PhotoPaths.TryParseUpload(blob.Name, out var id)) continue;
            Console.WriteLine($"{id}: {await processor.ProcessAsync(id, stop.Token)}");
        }
    } while (await timer.WaitForNextTickAsync(stop.Token));
}
catch (OperationCanceledException)
{
}
