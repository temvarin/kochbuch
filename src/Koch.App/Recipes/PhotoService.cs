using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Koch.App.Shell;
using Koch.Contracts;
using Microsoft.AspNetCore.Components.Forms;

namespace Koch.App.Recipes;

public sealed class PhotoUploadException(string message, Exception? inner = null) : Exception(message, inner);

public sealed class PhotoService(HttpClient http, AppText text)
{
    public const string Accept = "image/jpeg,image/png,image/webp";

    public static string Url(Guid photo, int width) => $"photos/{photo:D}/{width}.webp";

    public async Task<Guid> UploadAsync(IBrowserFile file, CancellationToken cancellationToken = default)
    {
        if (file.Size > PhotoPaths.MaxUploadBytes)
            throw new PhotoUploadException(text.Pick(
                $"Das Foto ist zu groß (höchstens {PhotoPaths.MaxUploadBytes / 1024 / 1024} MB).",
                $"Фото слишком большое (не больше {PhotoPaths.MaxUploadBytes / 1024 / 1024} МБ)."));
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
            throw new PhotoUploadException(text.Pick(
                "Das Foto konnte nicht hochgeladen werden. Ohne Verbindung geht das nicht – das Rezept selbst wird trotzdem gespeichert.",
                "Не удалось загрузить фото. Без подключения это невозможно — сам рецепт всё равно сохранится."), e);
        }
    }

    public static async Task<string> PreviewAsync(IBrowserFile file)
    {
        var small = await file.RequestImageFileAsync("image/jpeg", 600, 600);
        await using var stream = small.OpenReadStream(PhotoPaths.MaxUploadBytes);
        using var memory = new MemoryStream();
        await stream.CopyToAsync(memory);
        return $"data:image/jpeg;base64,{Convert.ToBase64String(memory.ToArray())}";
    }
}
