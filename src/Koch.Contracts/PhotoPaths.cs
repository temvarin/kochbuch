namespace Koch.Contracts;

/// <summary>
/// The blob layout Media owns. A photo is uploaded to <c>uploads/{id}</c>; Media turns it into
/// <c>photos/{id}/{width}.webp</c> for every width in <see cref="Widths"/> and deletes the upload.
/// </summary>
public static class PhotoPaths
{
    public const string Uploads = "uploads";
    public const string Photos = "photos";

    /// <summary>Phone photos are 2–8 MB; anything larger is refused.</summary>
    public const long MaxUploadBytes = 15 * 1024 * 1024;

    /// <summary>A list thumbnail and a full-width picture on a phone (2x density).</summary>
    public static IReadOnlyList<int> Widths { get; } = [400, 1200];

    public static string Upload(Guid id) => id.ToString("D");

    public static bool TryParseUpload(string blobName, out Guid id) => Guid.TryParseExact(blobName, "D", out id) && id != Guid.Empty;

    public static string Rendition(Guid id, int width) => $"{id:D}/{width}.webp";
}
