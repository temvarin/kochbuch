using Azure.Monitor.OpenTelemetry.AspNetCore;
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

string ConnectionString(IServiceProvider sp) =>
    sp.GetRequiredService<IConfiguration>().GetConnectionString("Database")
        ?? throw new InvalidOperationException("The connection string Database is missing.");

builder.Services.AddKeyedSingleton<IDocumentRows>("recipes", (sp, _) =>
    new SqlDocumentRows(ConnectionString(sp), "recipes", sp.GetRequiredService<ILogger<SqlDocumentRows>>()));
builder.Services.AddKeyedSingleton<IDocumentRows>("shopping", (sp, _) =>
    new SqlDocumentRows(ConnectionString(sp), "shopping", sp.GetRequiredService<ILogger<SqlDocumentRows>>()));

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
