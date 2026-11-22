using Koch.ServiceDefaults;
using Koch.ServiceDefaults.Documents;
using Koch.Shopping.Sync;
using Koch.Telemetry;

var builder = WebApplication.CreateBuilder(args);
builder.AddTelemetry("kochbuch-shopping");
builder.Services.AddGatewayUser();
builder.Services.AddHealthChecks();
builder.Services.AddSingleton<IDocumentRows>(services => new SqlDocumentRows(
    services.GetRequiredService<IConfiguration>().GetConnectionString("Shopping")
        ?? throw new InvalidOperationException("The connection string Shopping is missing."),
    "shopping", services.GetRequiredService<ILogger<SqlDocumentRows>>()));
var app = builder.Build();

app.UseAuthentication();
app.UseAuthorization();

// For the platform's probes: the process runs, whatever the database does.
app.MapHealthChecks("/healthz");

// The gateway forwards /api/shopping/{path} as /{path}, only for members.
app.MapGroup("").RequireAuthorization().MapPost("/sync", SyncEndpoint.Handle);

app.Run();

/// <summary>Entry point; public for the tests' <c>WebApplicationFactory</c>.</summary>
public partial class Program;
