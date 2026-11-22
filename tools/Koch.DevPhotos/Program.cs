using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Koch.Media.Contracts;
using Koch.Media.Uploads;
using Microsoft.Extensions.Logging.Abstractions;

// Usage, with Azurite running (npx azurite-blob --location <folder>):
//   dotnet run --project tools/Koch.DevPhotos -- [origin of the app, default http://localhost:8091]
// Creates the containers and the CORS rule the app needs, then processes every new upload every two seconds, the way
// Event Grid and the Media function do in Azure (docs/adr/0006). Ctrl+C stops it.
const string Azurite = "UseDevelopmentStorage=true";
var origin = args.Length > 0 ? args[0] : "http://localhost:8091";

var service = new BlobServiceClient(Azurite);
foreach (var name in new[] { PhotoPaths.Uploads, PhotoPaths.Photos })
    await service.GetBlobContainerClient(name).CreateIfNotExistsAsync();
await service.SetPropertiesAsync(new BlobServiceProperties
{
    Cors =
    [
        new BlobCorsRule
        {
            AllowedOrigins = origin,
            AllowedMethods = "PUT,OPTIONS",
            AllowedHeaders = "content-type,x-ms-blob-type,x-ms-version,x-ms-client-request-id",
            ExposedHeaders = "x-ms-request-id",
            MaxAgeInSeconds = 3600,
        },
    ],
});
Console.WriteLine($"Azurite ready for uploads from {origin}. Processing uploads; Ctrl+C stops.");

#pragma warning disable RS0030 // a development tool, not app code: real time is what it needs
var processor = new PhotoProcessor(new BlobPhotoBlobs(service), TimeProvider.System, NullLogger<PhotoProcessor>.Instance);
#pragma warning restore RS0030
var uploads = service.GetBlobContainerClient(PhotoPaths.Uploads);
using var stop = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => (e.Cancel, _) = (true, stop.CancelAsync());
#pragma warning disable RS0030 // a development tool, not app code: real time is what it needs
using var timer = new PeriodicTimer(TimeSpan.FromSeconds(2));
#pragma warning restore RS0030
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
