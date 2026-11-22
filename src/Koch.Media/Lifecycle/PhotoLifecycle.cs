using System.Diagnostics;
using System.Text.Json;
using Azure.Messaging;
using Koch.Media.Contracts;
using Koch.Media.Uploads;
using Koch.Recipes.Events;
using Microsoft.Extensions.Logging;

namespace Koch.Media.Lifecycle;

public enum TagOutcome
{
    Applied,
    /// <summary>The rendition already has a newer event's tags: this one came late or twice.</summary>
    Stale,
    Missing,
}

public enum LifecycleOutcome
{
    Applied,
    Stale,
    /// <summary>No renditions and no upload: never uploaded, refused, or already swept.</summary>
    NoPhoto,
}

/// <summary>A photo event from Recipes, as Media needs it.</summary>
/// <param name="TraceParent">The trace of the request that caused it, if the event says (docs/adr/0009).</param>
public sealed record PhotoEvent(Guid Photo, PhotoUsage Usage, string? TraceParent = null)
{
    /// <summary>Reads the CloudEvent Recipes publishes (Koch.Recipes.Events).</summary>
    /// <returns>null for anything else: logged and dropped, since retrying can't fix it.</returns>
    public static PhotoEvent? TryParse(BinaryData message)
    {
        try
        {
            var cloudEvent = CloudEvent.Parse(message);
            if (cloudEvent?.Data is null || cloudEvent.Time is not { } time) return null;
            var state = cloudEvent.Type switch
            {
                PhotoEvents.Attached => PhotoUsage.Attached,
                PhotoEvents.Detached => PhotoUsage.Detached,
                _ => null,
            };
            if (state is null) return null;
            if (!cloudEvent.ExtensionAttributes.TryGetValue(PhotoEvents.SequenceAttribute, out var value)
                || value?.ToString() is not { Length: 20 } sequence || !sequence.All(char.IsAsciiDigit)) return null;
            var usage = JsonSerializer.Deserialize(cloudEvent.Data.ToString(), PhotoEventsJson.Default.PhotoUsageJson);
            if (usage is null || usage.Photo == Guid.Empty) return null;
            return new PhotoEvent(usage.Photo, new PhotoUsage(state, sequence, DateOnly.FromDateTime(time.UtcDateTime)),
                TraceParentOf(cloudEvent));
        }
        catch (Exception e) when (e is ArgumentException or JsonException or FormatException or InvalidOperationException)
        {
            return null;
        }
    }

    /// <summary>Optional: an event without one, or with one that isn't valid, is still applied.</summary>
    private static string? TraceParentOf(CloudEvent cloudEvent) =>
        cloudEvent.ExtensionAttributes.TryGetValue(PhotoEvents.TraceParentAttribute, out var value)
            && value?.ToString() is { } traceParent && ActivityContext.TryParse(traceParent, null, out _)
            ? traceParent : null;
}

/// <summary>
/// Applies a photo event to the photo's renditions. Safe in any order and any number of times: each rendition takes the
/// event only if it is newer than what it has (<see cref="PhotoUsage"/>).
/// <para>
/// The event can come before the upload's own BlobCreated has been processed: the phone saves the recipe right after
/// uploading. Then Media processes the upload here. Renditions are only ever created, never overwritten, so the
/// upload's own message can't later reset the tags this event sets (docs/adr/0008).
/// </para>
/// </summary>
public sealed partial class PhotoLifecycle(IPhotoBlobs blobs, PhotoProcessor processor, ILogger<PhotoLifecycle> logger)
{
    public async Task<LifecycleOutcome> ApplyAsync(PhotoEvent photoEvent, CancellationToken cancellationToken)
    {
        var processed = false;
        List<TagOutcome> outcomes = [];
        foreach (var width in PhotoPaths.Widths)
        {
            var outcome = await blobs.TagRenditionAsync(photoEvent.Photo, width, photoEvent.Usage, cancellationToken);
            if (outcome == TagOutcome.Missing && !processed)
            {
                processed = true;
                var made = await processor.ProcessAsync(photoEvent.Photo, cancellationToken);
                ProcessedEarly(logger, photoEvent.Photo, made);
                outcome = await blobs.TagRenditionAsync(photoEvent.Photo, width, photoEvent.Usage, cancellationToken);
            }
            outcomes.Add(outcome);
        }
        return outcomes.Contains(TagOutcome.Applied) ? LifecycleOutcome.Applied
            : outcomes.TrueForAll(o => o == TagOutcome.Missing) ? LifecycleOutcome.NoPhoto
            : LifecycleOutcome.Stale;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Photo {Id} had no renditions yet when its event came; processing: {Outcome}.")]
    private static partial void ProcessedEarly(ILogger logger, Guid id, Outcome outcome);
}

/// <summary>Deletes the renditions no recipe has shown for <see cref="PhotoUsage.GraceDays"/> days.</summary>
public sealed partial class PhotoSweeper(IPhotoBlobs blobs, TimeProvider clock, ILogger<PhotoSweeper> logger)
{
    /// <returns>How many renditions were deleted.</returns>
    public async Task<int> SweepAsync(CancellationToken cancellationToken)
    {
        var before = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime).AddDays(-PhotoUsage.GraceDays);
        var deleted = 0;
        await foreach (var name in blobs.FindDetachedAsync(before, cancellationToken))
        {
            // Deleted only if still detached since before then: a photo attached again since the search stays.
            if (await blobs.DeleteIfDetachedAsync(name, before, cancellationToken)) deleted++;
        }
        Swept(logger, deleted, before);
        return deleted;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Deleted {Count} renditions detached before {Day}.")]
    private static partial void Swept(ILogger logger, int count, DateOnly day);
}
