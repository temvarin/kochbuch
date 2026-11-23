using Koch.Gateway.Account;
using Koch.Gateway.Hosting;
using Koch.Gateway.Photos;
using Koch.Gateway.Proxy;
using Koch.Telemetry;

var builder = WebApplication.CreateBuilder(args);
builder.AddTelemetry("kochbuch-gateway");
builder.Services.AddHouseholdSignIn();
builder.Services.AddServiceProxy();
builder.Services.AddPhotoStorage();
builder.Services.AddHealthChecks();
#pragma warning disable RS0030 // The composition root: the one place that picks the real clock.
builder.Services.AddSingleton(TimeProvider.System);
#pragma warning restore RS0030
var app = builder.Build();

// Uploads go from the phone straight to Blob Storage; only that origin is added to connect-src.
app.UseSecurityHeaders(PhotoUploadOrigin(app.Configuration));
// After the security headers, so that 401 and 403 answers carry them too.
app.UseAuthentication();
app.UseAuthorization();

app.MapStaticAssets();
// For the platform's probes. Easy Auth lets every request through (AllowAnonymous, infra/modules/gatewayAuth.bicep),
// so the probe needs no sign-in.
app.MapHealthChecks("/healthz");

app.MapWhoAmI();
app.MapPhotos();
// /api/{service}/... goes to the services, members only.
app.MapServiceProxy();
// Everything else under /api is for members only too. Easy Auth lets everyone through, because the app must load
// without a sign-in, so this is the gate. Unknown API routes are errors, not app pages.
app.MapGroup("/api").RequireAuthorization(Members.Policy).Map("{**path}", () => Results.NotFound());

// Routes like /rezepte exist only inside the app. The start page answers them and is never cached, so that a new
// release is picked up.
app.MapFallbackToFile("index.html", new StaticFileOptions
{
    OnPrepareResponse = context => context.Context.Response.Headers.CacheControl = "no-cache",
});

app.Run();

/// <summary>Entry point; public for the tests' <c>WebApplicationFactory</c>.</summary>
public partial class Program
{
    private static string? PhotoUploadOrigin(IConfiguration configuration) =>
        configuration["Photos:BlobServiceUri"] is { Length: > 0 } uri ? new Uri(uri).GetLeftPart(UriPartial.Authority)
        : configuration["Photos:UploadOrigin"]; // locally: the emulator, e.g. http://127.0.0.1:10000
}
