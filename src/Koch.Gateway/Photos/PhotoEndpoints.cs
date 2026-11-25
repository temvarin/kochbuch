using Koch.Contracts;
using Koch.Gateway.Account;

namespace Koch.Gateway.Photos;

/// <summary>
/// Photos at the gateway: room for an upload, and renditions served from the app's own origin, so the Content Security
/// Policy keeps <c>img-src 'self'</c> and the service worker can keep them for offline use (docs/adr/0006).
/// </summary>
public static class PhotoEndpoints
{
    /// <summary>A rendition never changes (a new photo gets a new id), so the phone may keep it forever.</summary>
    public const string Immutable = "private, max-age=31536000, immutable";

    public static void MapPhotos(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/photos/upload-slot", async (IPhotoStorage storage, CancellationToken cancellationToken) =>
                Results.Json(await storage.NewUploadSlotAsync(cancellationToken), GatewayJson.Default.UploadSlot))
            .RequireAuthorization(Members.Policy);

        app.MapGet("/photos/{photo:guid}/{width:int}.webp", async (Guid photo, int width, HttpContext context,
                IPhotoStorage storage, CancellationToken cancellationToken) =>
            {
                if (!PhotoPaths.Widths.Contains(width)) return Results.NotFound();
                if (await storage.OpenRenditionAsync(photo, width, cancellationToken) is not { } stream)
                    return Results.NotFound(); // not processed yet; the app shows a placeholder and asks again later
                context.Response.Headers.CacheControl = Immutable;
                return Results.Stream(stream, "image/webp");
            })
            .RequireAuthorization(Members.Policy);
    }
}
