using System.Text.Json;
using Koch.Media.Contracts;
using Koch.Media.Imaging;
using Koch.Media.Lifecycle;
using Microsoft.Extensions.Logging;

namespace Koch.Media.Uploads;

/// <summary>
/// Media's blobs: the uploads to read and drop, the renditions to write, tag and delete (<see cref="PhotoPaths"/>,
/// <see cref="PhotoUsage"/>).
/// </summary>
public interface IPhotoBlobs
{
    /// <returns>null if there is no such upload (anymore).</returns>
    Task<byte[]?> ReadUploadAsync(Guid id, CancellationToken cancellationToken);

    Task DeleteUploadAsync(Guid id, CancellationToken cancellationToken);

    Task<bool> HasRenditionsAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>
    /// Creates the rendition with these tags, or leaves an existing one as it is: the same upload always gives the same
    /// bytes, and its tags may already say more than <paramref name="usage"/> does (docs/adr/0008).
    /// </summary>
    Task CreateRenditionAsync(Guid id, Rendition rendition, PhotoUsage usage, CancellationToken cancellationToken);

    /// <summary>Sets the rendition's tags to <paramref name="usage"/> if its sequence is higher than the stored one.</summary>
    Task<TagOutcome> TagRenditionAsync(Guid id, int width, PhotoUsage usage, CancellationToken cancellationToken);

    /// <summary>The renditions (blob names in <c>photos/</c>) detached before the day, by their tags.</summary>
    IAsyncEnumerable<string> FindDetachedAsync(DateOnly before, CancellationToken cancellationToken);

    /// <returns>false if the rendition is gone, or no longer detached since before the day.</returns>
    Task<bool> DeleteIfDetachedAsync(string name, DateOnly before, CancellationToken cancellationToken);
}

public enum Outcome
{
    Done,
    /// <summary>Delivered again after it was done (Service Bus delivers at least once): nothing to do.</summary>
    AlreadyDone,
    /// <summary>The upload is gone and nothing was made from it, e.g. expired by the lifecycle rule.</summary>
    Missing,
    /// <summary>Not a photo Media can use: dropped, never retried.</summary>
    Refused,
}

/// <summary>
/// What happens to one upload. Safe to run twice for the same photo, also at the same time: renditions are created only
/// if missing, and the upload is deleted only after they are written, so a crash in between leaves work to redo, never a
/// lost photo (DDIA ch12 p.493, at-least-once delivery; docs/adr/0006, 0008).
/// </summary>
public sealed partial class PhotoProcessor(IPhotoBlobs blobs, TimeProvider clock, ILogger<PhotoProcessor> logger)
{
    public async Task<Outcome> ProcessAsync(Guid id, CancellationToken cancellationToken)
    {
        var upload = await blobs.ReadUploadAsync(id, cancellationToken);
        if (upload is null)
        {
            var done = await blobs.HasRenditionsAsync(id, cancellationToken);
            UploadMissing(logger, id, done);
            return done ? Outcome.AlreadyDone : Outcome.Missing;
        }

        IReadOnlyList<Rendition> renditions;
        try
        {
            renditions = Renditions.Make(upload);
        }
        catch (NotAPhotoException e)
        {
            UploadRefused(logger, id, e.Message);
            await blobs.DeleteUploadAsync(id, cancellationToken);
            return Outcome.Refused;
        }

        var fresh = PhotoUsage.Fresh(DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime));
        foreach (var rendition in renditions) await blobs.CreateRenditionAsync(id, rendition, fresh, cancellationToken);
        await blobs.DeleteUploadAsync(id, cancellationToken);
        return Outcome.Done;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Upload {Id} is gone; renditions exist: {Done}.")]
    private static partial void UploadMissing(ILogger logger, Guid id, bool done);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Upload {Id} refused: {Reason}")]
    private static partial void UploadRefused(ILogger logger, Guid id, string reason);
}

/// <summary>
/// Event Grid's BlobCreated event, as it arrives in the Service Bus queue. Only the subject matters: it names the blob,
/// and the blob name is the photo's id.
/// </summary>
public static class BlobCreated
{
    private const string Prefix = "/blobServices/default/containers/" + PhotoPaths.Uploads + "/blobs/";

    /// <returns>false for anything that is not a BlobCreated of an upload Media made room for.</returns>
    public static bool TryParse(string message, out Guid id)
    {
        id = Guid.Empty;
        try
        {
            using var document = JsonDocument.Parse(message);
            // Event Grid sends one event per message; an array of one is accepted too.
            var root = document.RootElement.ValueKind == JsonValueKind.Array && document.RootElement.GetArrayLength() == 1
                ? document.RootElement[0]
                : document.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return false;
            var type = root.TryGetProperty("eventType", out var t) ? t.GetString() : root.TryGetProperty("type", out var c) ? c.GetString() : null;
            if (type != "Microsoft.Storage.BlobCreated") return false;
            var subject = root.TryGetProperty("subject", out var s) ? s.GetString() : null;
            return subject is not null && subject.StartsWith(Prefix, StringComparison.Ordinal)
                && PhotoPaths.TryParseUpload(subject[Prefix.Length..], out id);
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
