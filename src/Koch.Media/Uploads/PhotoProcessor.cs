using System.Text.Json;
using Koch.Contracts;
using Microsoft.Extensions.Logging;

namespace Koch.Media.Uploads;

public interface IPhotoBlobs
{
    Task<byte[]?> ReadUploadAsync(Guid id, CancellationToken cancellationToken);

    Task DeleteUploadAsync(Guid id, CancellationToken cancellationToken);

    Task<bool> HasRenditionsAsync(Guid id, CancellationToken cancellationToken);

    Task CreateRenditionAsync(Guid id, Rendition rendition, CancellationToken cancellationToken);
}

public enum Outcome
{
    Done,
    AlreadyDone,
    Missing,
    Refused,
}

public sealed partial class PhotoProcessor(IPhotoBlobs blobs, ILogger<PhotoProcessor> logger)
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

        foreach (var rendition in renditions) await blobs.CreateRenditionAsync(id, rendition, cancellationToken);
        await blobs.DeleteUploadAsync(id, cancellationToken);
        return Outcome.Done;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Upload {Id} is gone; renditions exist: {Done}.")]
    private static partial void UploadMissing(ILogger logger, Guid id, bool done);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Upload {Id} refused: {Reason}")]
    private static partial void UploadRefused(ILogger logger, Guid id, string reason);
}

public static class BlobCreated
{
    private const string Prefix = "/blobServices/default/containers/" + PhotoPaths.Uploads + "/blobs/";

    public static bool TryParse(string message, out Guid id)
    {
        id = Guid.Empty;
        try
        {
            using var document = JsonDocument.Parse(message);
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
