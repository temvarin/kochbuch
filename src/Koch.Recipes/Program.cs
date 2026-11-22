using Azure.Identity;
using Azure.Messaging.ServiceBus;
using Koch.Recipes.Contracts;
using Koch.Recipes.Events;
using Koch.Recipes.Outbox;
using Koch.Recipes.Storage;
using Koch.Recipes.Sync;
using Koch.ServiceDefaults;
using Koch.ServiceDefaults.Documents;
using Koch.Telemetry;

var builder = WebApplication.CreateBuilder(args);
builder.AddTelemetry("kochbuch-recipes", OutboxRelay.ActivitySourceName);
builder.Services.AddGatewayUser();
builder.Services.AddHealthChecks();
#pragma warning disable RS0030 // The composition root: the one place that picks the real clock.
builder.Services.AddSingleton(TimeProvider.System);
#pragma warning restore RS0030

string ConnectionString(IServiceProvider services) =>
    services.GetRequiredService<IConfiguration>().GetConnectionString("Recipes")
        ?? throw new InvalidOperationException("The connection string Recipes is missing.");
// A recipe that changes its photo saves photo events with it, in the same transaction (docs/adr/0008).
builder.Services.AddSingleton<IDocumentRows>(services => new SqlDocumentRows(ConnectionString(services), "recipes",
    services.GetRequiredService<ILogger<SqlDocumentRows>>(), Rows.PhotoEvents));
builder.Services.AddSingleton<IOutbox>(services => new SqlOutbox(ConnectionString(services), "recipes"));
builder.Services.AddSingleton<OutboxWakeUp>();

// The relay runs where Service Bus is configured, i.e. in Azure. Locally the events stay in recipes.Outbox, to look at.
if (builder.Configuration["ServiceBus:FullyQualifiedNamespace"] is { Length: > 0 } serviceBus)
{
    builder.Services.AddSingleton(services => new ServiceBusClient(serviceBus, new DefaultAzureCredential(
        new DefaultAzureCredentialOptions { ManagedIdentityClientId = services.GetRequiredService<IConfiguration>()["AZURE_CLIENT_ID"] })));
    builder.Services.AddSingleton<IEventPublisher>(services =>
        new ServiceBusPublisher(services.GetRequiredService<ServiceBusClient>().CreateSender(PhotoEvents.Queue)));
    builder.Services.AddHostedService<OutboxRelay>();
}
var app = builder.Build();

app.UseAuthentication();
app.UseAuthorization();

// For the platform's probes: the process runs. Says nothing about the database, so that a database outage doesn't
// get the replicas restarted in a loop.
app.MapHealthChecks("/healthz");

// The gateway forwards /api/recipes/{path} as /{path}, only for members.
var api = app.MapGroup("").RequireAuthorization();
api.MapGet("/status", async (HttpContext context, IDocumentRows rows, CancellationToken cancellationToken) =>
    Results.Json(new RecipesStatus(context.User.UserId(), await rows.CanReadAsync(cancellationToken)),
        RecipesJson.Default.RecipesStatus));
api.MapPost("/sync", SyncEndpoint.Handle);

app.Run();

/// <summary>Entry point; public for the tests' <c>WebApplicationFactory</c>.</summary>
public partial class Program;
