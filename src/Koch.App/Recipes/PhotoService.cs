using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Koch.Gateway.Contracts;
using Koch.Media.Contracts;
using Microsoft.AspNetCore.Components.Forms;

namespace Koch.App.Recipes;

/// <summary>Why an upload didn't work, in German for the user.</summary>
public sealed class PhotoUploadException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>
/// Uploads a photo: the gateway makes room for it, and the phone puts the file straight into Blob Storage. Media then
/// makes the renditions; until it has, the photo's address answers 404 and the app shows the preview or a placeholder.
/// Needs a connection; the recipe itself is saved offline as always (docs/adr/0006).
/// </summary>
public sealed class PhotoService(HttpClient http)
{
    /// <summary>JPEG, PNG and WebP. Phones convert HEIC to JPEG for a file input that asks for these.</summary>
    public const string Accept = "image/jpeg,image/png,image/webp";

    /// <summary>The address of a rendition; served by the gateway from the app's own origin.</summary>
    public static string Url(Guid photo, int width) => $"photos/{photo:D}/{width}.webp";

    /// <returns>The photo's id, to save in the recipe.</returns>
    /// <exception cref="PhotoUploadException">Too large, no connection, or refused.</exception>
    public async Task<Guid> UploadAsync(IBrowserFile file, CancellationToken cancellationToken = default)
    {
        if (file.Size > PhotoPaths.MaxUploadBytes)
            throw new PhotoUploadException($"Das Foto ist zu groß (höchstens {PhotoPaths.MaxUploadBytes / 1024 / 1024} MB).");
        try
        {
            using var slotResponse = await http.PostAsync(new Uri("api/photos/upload-slot", UriKind.Relative), null, cancellationToken);
            slotResponse.EnsureSuccessStatusCode();
            var slot = await slotResponse.Content.ReadFromJsonAsync(GatewayJson.Default.UploadSlot, cancellationToken)
                ?? throw new JsonException("No upload slot.");

            using var content = new StreamContent(file.OpenReadStream(PhotoPaths.MaxUploadBytes, cancellationToken));
            content.Headers.ContentType = new MediaTypeHeaderValue(file.ContentType is { Length: > 0 } type ? type : "application/octet-stream");
            using var request = new HttpRequestMessage(HttpMethod.Put, slot.UploadUrl) { Content = content };
            request.Headers.Add("x-ms-blob-type", "BlockBlob");
            using var upload = await http.SendAsync(request, cancellationToken);
            upload.EnsureSuccessStatusCode();
            return slot.Photo;
        }
        catch (Exception e) when (e is HttpRequestException or JsonException or TaskCanceledException or IOException)
        {
            throw new PhotoUploadException("Das Foto konnte nicht hochgeladen werden. Ohne Verbindung geht das nicht – das Rezept selbst wird trotzdem gespeichert.", e);
        }
    }

    /// <summary>A small preview of the chosen file, made by the browser, to show before Media has processed it.</summary>
    public static async Task<string> PreviewAsync(IBrowserFile file)
    {
        var small = await file.RequestImageFileAsync("image/jpeg", 600, 600);
        await using var stream = small.OpenReadStream(PhotoPaths.MaxUploadBytes);
        using var memory = new MemoryStream();
        await stream.CopyToAsync(memory);
        return $"data:image/jpeg;base64,{Convert.ToBase64String(memory.ToArray())}";
    }
}
