using Azure.Identity;
using Azure.Monitor.OpenTelemetry.Exporter;
using Azure.Storage.Blobs;
using Koch.Media.Uploads;
using Microsoft.Azure.Functions.Worker.Builder;
using Microsoft.Azure.Functions.Worker.OpenTelemetry;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OpenTelemetry.Resources;

var builder = FunctionsApplication.CreateBuilder(args);

if (builder.Configuration["APPLICATIONINSIGHTS_CONNECTION_STRING"] is { Length: > 0 })
{
    builder.Services.AddOpenTelemetry()
        .ConfigureResource(resource => resource.AddService("kochbuch-media"))
        .UseFunctionsWorkerDefaults()
        .UseAzureMonitorExporter(options => (options.SamplingRatio, options.TracesPerSecond) = (1F, null));
}

builder.Services.AddSingleton(services =>
{
    var configuration = services.GetRequiredService<IConfiguration>();
    if (configuration["Photos:ConnectionString"] is { Length: > 0 } local) return new BlobServiceClient(local);
    var uri = configuration["Photos:BlobServiceUri"] ?? throw new InvalidOperationException("The setting Photos:BlobServiceUri is missing.");
    return new BlobServiceClient(new Uri(uri),
        new DefaultAzureCredential(new DefaultAzureCredentialOptions { ManagedIdentityClientId = configuration["AZURE_CLIENT_ID"] }));
});
builder.Services.AddSingleton<IPhotoBlobs, BlobPhotoBlobs>();
builder.Services.AddSingleton<PhotoProcessor>();

builder.Build().Run();
