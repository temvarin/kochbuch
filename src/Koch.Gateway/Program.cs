using Azure.Monitor.OpenTelemetry.AspNetCore;
using Koch.Gateway.Account;
using Koch.Gateway.Hosting;
using Koch.Gateway.Photos;
using Koch.Gateway.Proxy;
using OpenTelemetry.Instrumentation.AspNetCore;
using OpenTelemetry.Resources;

var builder = WebApplication.CreateBuilder(args);

if (builder.Configuration["APPLICATIONINSIGHTS_CONNECTION_STRING"] is { Length: > 0 })
{
    builder.Services.AddOpenTelemetry()
        .UseAzureMonitor(options => (options.SamplingRatio, options.TracesPerSecond) = (1F, null))
        .ConfigureResource(resource => resource.AddService("kochbuch-gateway"));
    builder.Services.Configure<AspNetCoreTraceInstrumentationOptions>(options =>
        options.Filter = context => !context.Request.Path.StartsWithSegments("/healthz"));
}

builder.Services.AddHouseholdSignIn();
builder.Services.AddServiceProxy();
builder.Services.AddPhotoStorage();
builder.Services.AddHealthChecks();
builder.Services.AddSingleton(TimeProvider.System);

var app = builder.Build();

app.UseSecurityHeaders(PhotoUploadOrigin(app.Configuration));
app.UseAuthentication();
app.UseAuthorization();

app.MapStaticAssets();
app.MapHealthChecks("/healthz");

app.MapWhoAmI();
app.MapPhotos();
app.MapServiceProxy();
app.MapGroup("/api").RequireAuthorization(Members.Policy).Map("{**path}", () => Results.NotFound());

app.MapFallbackToFile("index.html", new StaticFileOptions
{
    OnPrepareResponse = context => context.Context.Response.Headers.CacheControl = "no-cache",
});

app.Run();

static string? PhotoUploadOrigin(IConfiguration configuration) =>
    configuration["Photos:BlobServiceUri"] is { Length: > 0 } uri ? new Uri(uri).GetLeftPart(UriPartial.Authority)
    : configuration["Photos:UploadOrigin"];
