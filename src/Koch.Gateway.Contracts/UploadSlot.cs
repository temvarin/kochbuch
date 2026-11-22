namespace Koch.Gateway.Contracts;

/// <summary>
/// The answer of <c>POST /api/photos/upload-slot</c>: where the phone may put one photo, straight into Blob Storage,
/// without the bytes passing the gateway (valet key). The link allows creating that one blob and nothing else, for a
/// few minutes (docs/adr/0006).
/// </summary>
/// <param name="Photo">The id to save in the recipe once the upload is done.</param>
/// <param name="UploadUrl">PUT the file here with the header <c>x-ms-blob-type: BlockBlob</c>.</param>
/// <param name="MaxBytes">Larger files are refused when processed.</param>
public sealed record UploadSlot(Guid Photo, Uri UploadUrl, DateTimeOffset Expires, long MaxBytes);
