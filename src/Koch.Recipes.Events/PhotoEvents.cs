using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Koch.Recipes.Events;

/// <summary>
/// A recipe started or stopped showing a photo. Published as a CloudEvent (structured JSON) into the Service Bus queue
/// <see cref="PhotoEvents.Queue"/>; samples of every released version are in tests/Koch.Recipes.Tests/Contracts/events.
/// </summary>
/// <param name="Photo">The photo's id (Koch.Media.Contracts.PhotoPaths).</param>
/// <param name="Recipe">The recipe that attached or detached it: for the logs, the consumer needs no more.</param>
public sealed record PhotoUsageJson(Guid Photo, Guid Recipe);

/// <summary>The names of the photo events and how they are ordered (docs/adr/0008).</summary>
public static class PhotoEvents
{
    /// <summary>The Service Bus queue; one consumer, so no topic (docs/adr/0007).</summary>
    public const string Queue = "photo-lifecycle";

    /// <summary>The CloudEvent's <c>source</c>.</summary>
    public const string Source = "/kochbuch/recipes";

    /// <summary>A recipe now shows the photo: keep it.</summary>
    public const string Attached = "koch.recipes.photo.attached";

    /// <summary>No recipe shows the photo any more (changed, removed, or its recipe deleted).</summary>
    public const string Detached = "koch.recipes.photo.detached";

    /// <summary>
    /// The CloudEvents "sequence" extension attribute. Events about the same photo arrive in any order (retries, two
    /// consumers at once); the one with the higher sequence is the newer, compared as strings.
    /// </summary>
    public const string SequenceAttribute = "sequence";

    /// <summary>
    /// The CloudEvents distributed-tracing extension: where in a trace the event was caused (W3C <c>traceparent</c>).
    /// Optional; a consumer that can't read it still applies the event (docs/adr/0009).
    /// </summary>
    public const string TraceParentAttribute = "traceparent";

    /// <summary>Twenty digits, so that comparing as strings is comparing as numbers.</summary>
    public static string Sequence(long value) => value.ToString("D20", CultureInfo.InvariantCulture);

    public static string Subject(Guid photo) => $"photos/{photo:D}";

    public static string Data(PhotoUsageJson usage) => JsonSerializer.Serialize(usage, PhotoEventsJson.Default.PhotoUsageJson);
}

[JsonSourceGenerationOptions(JsonSerializerDefaults.Web,
    RespectNullableAnnotations = true, RespectRequiredConstructorParameters = true)]
[JsonSerializable(typeof(PhotoUsageJson))]
public sealed partial class PhotoEventsJson : JsonSerializerContext;
