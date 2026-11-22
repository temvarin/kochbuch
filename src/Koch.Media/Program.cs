using Azure.Identity;
using Azure.Monitor.OpenTelemetry.Exporter;
using Azure.Storage.Blobs;
using Koch.Media.Lifecycle;
using Koch.Media.Uploads;
using Microsoft.Azure.Functions.Worker.Builder;
using Microsoft.Azure.Functions.Worker.OpenTelemetry;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OpenTelemetry.Resources;

var builder = FunctionsApplication.CreateBuilder(args);

// Traces, metrics and logs with OpenTelemetry into Application Insights, like the HTTP apps (Koch.Telemetry); the
// Functions host does the same for its side (host.json, telemetryMode). Locally nowhere (docs/adr/0009).
if (builder.Configuration["APPLICATIONINSIGHTS_CONNECTION_STRING"] is { Length: > 0 })
{
    builder.Services.AddOpenTelemetry()
        .ConfigureResource(resource => resource.AddService("kochbuch-media"))
        .WithTracing(tracing => tracing.AddSource(PhotoTracing.SourceName))
        .UseFunctionsWorkerDefaults()
        // Every trace, as in the HTTP apps: a few photos a week.
        .UseAzureMonitorExporter(options => (options.SamplingRatio, options.TracesPerSecond) = (1F, null));
}

// Blob Storage with Media's own managed identity (AZURE_CLIENT_ID); no key or connection string anywhere. Locally,
// Photos:ConnectionString points at Azurite instead.
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
builder.Services.AddSingleton<PhotoLifecycle>();
builder.Services.AddSingleton<PhotoSweeper>();
#pragma warning disable RS0030 // The composition root: the one place that picks the real clock.
builder.Services.AddSingleton(TimeProvider.System);
#pragma warning restore RS0030

builder.Build().Run();
