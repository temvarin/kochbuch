using Azure.Identity;
using Azure.Monitor.OpenTelemetry.AspNetCore;
using Azure.Storage.Blobs;
using Koch.Api;
using Koch.Api.Storage;
using Koch.Contracts;
using OpenTelemetry.Instrumentation.AspNetCore;
using OpenTelemetry.Resources;

var builder = WebApplication.CreateBuilder(args);

if (builder.Configuration["APPLICATIONINSIGHTS_CONNECTION_STRING"] is { Length: > 0 })
{
    builder.Services.AddOpenTelemetry()
        .UseAzureMonitor(options => (options.SamplingRatio, options.TracesPerSecond) = (1F, null))
        .ConfigureResource(resource => resource.AddService("kochbuch-api"));
    builder.Services.Configure<AspNetCoreTraceInstrumentationOptions>(options =>
        options.Filter = context => !context.Request.Path.StartsWithSegments("/healthz"));
}

builder.Services.AddGatewayUser();
builder.Services.AddHealthChecks();

IDocumentRows CreateStore(IServiceProvider sp, string name)
{
    var config = sp.GetRequiredService<IConfiguration>();
    var logger = sp.GetRequiredService<ILogger<BlobDocumentRows>>();
    if (config["Storage:BlobServiceUri"] is { Length: > 0 } uri)
        return new BlobDocumentRows(new BlobServiceClient(new Uri(uri), new DefaultAzureCredential()).GetBlobContainerClient("documents"), $"{name}.json", logger);
    if (config["Storage:ConnectionString"] is { Length: > 0 } cs)
        return new BlobDocumentRows(new BlobServiceClient(cs).GetBlobContainerClient("documents"), $"{name}.json", logger);
    if (builder.Environment.IsDevelopment())
        return new FileDocumentRows(Path.Combine(builder.Environment.ContentRootPath, ".dev-data", $"{name}.json"));
    throw new InvalidOperationException("The configuration value Storage:BlobServiceUri is missing.");
}

builder.Services.AddKeyedSingleton("recipes", (sp, _) => CreateStore(sp, "recipes"));
builder.Services.AddKeyedSingleton("shopping", (sp, _) => CreateStore(sp, "shopping"));

var app = builder.Build();

app.UseAuthentication();
app.UseAuthorization();
app.MapHealthChecks("/healthz");

var api = app.MapGroup("").RequireAuthorization();
api.MapGet("/recipes/status", async (
    HttpContext context,
    [FromKeyedServices("recipes")] IDocumentRows rows,
    CancellationToken cancellationToken) =>
    Results.Json(new RecipesStatus(context.User.UserId(), await rows.CanReadAsync(cancellationToken)), RecipesJson.Default.RecipesStatus));
api.MapPost("/recipes/sync", Koch.Api.Recipes.SyncEndpoint.Handle);
api.MapPost("/shopping/sync", Koch.Api.Shopping.SyncEndpoint.Handle);

app.Run();
