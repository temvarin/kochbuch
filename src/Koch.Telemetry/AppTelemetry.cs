using System.Diagnostics;
using Azure.Monitor.OpenTelemetry.AspNetCore;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry.Instrumentation.AspNetCore;
using OpenTelemetry.Resources;

namespace Koch.Telemetry;

/// <summary>
/// Traces, metrics and logs with OpenTelemetry, sent to Application Insights (docs/adr/0009). ASP.NET Core, HttpClient
/// (and so YARP), SqlClient and the Azure SDK record their own spans; the trace context travels in the
/// <c>traceparent</c> header from the gateway to the services, and through the outbox to Media.
/// </summary>
public static class AppTelemetry
{
    /// <summary>The platform's probe, every 30 seconds while a replica runs: not worth a trace.</summary>
    public const string HealthPath = "/healthz";

    /// <summary>
    /// Sends telemetry where Azure configured Application Insights; nowhere otherwise (locally, tests).
    /// </summary>
    /// <param name="serviceName">The role name in Application Insights, e.g. <c>kochbuch-recipes</c>.</param>
    /// <param name="sources">The service's own activity sources.</param>
    public static WebApplicationBuilder AddTelemetry(this WebApplicationBuilder builder, string serviceName, params string[] sources)
    {
        if (builder.Configuration["APPLICATIONINSIGHTS_CONNECTION_STRING"] is not { Length: > 0 }) return builder;

        builder.Services.AddOpenTelemetry()
            .UseAzureMonitor(options =>
            {
                // Every trace, whole: two people make a few hundred requests a day, and a sampled-away trace is the one
                // you need. The workspace's daily cap bounds the cost if that ever changes (infra/modules/observability.bicep).
                options.SamplingRatio = 1F;
                options.TracesPerSecond = null;
            })
            .ConfigureResource(resource => resource.AddService(serviceName))
            .WithTracing(tracing => tracing.AddSource(sources));
        builder.Services.Configure<AspNetCoreTraceInstrumentationOptions>(options =>
            options.Filter = context => !context.Request.Path.StartsWithSegments(HealthPath));
        return builder;
    }

    /// <summary>
    /// The current trace position as a W3C <c>traceparent</c>, to store with work that continues later elsewhere (the
    /// outbox); null outside a trace.
    /// </summary>
    public static string? TraceParent() => Activity.Current is { IdFormat: ActivityIdFormat.W3C } activity ? activity.Id : null;
}
