using System.Net;
using System.Net.Http.Json;
using Koch.Gateway.Contracts;
using Koch.Gateway.Photos;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace Koch.Gateway.Tests;

/// <summary>Photos at the gateway: room for uploads, renditions for members only (docs/adr/0006).</summary>
public sealed class PhotoTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    private const string Member = "8a1c0e52-member";
    private static readonly Guid Photo = new("3c9e8f10-5a1b-4c2d-8e3f-4a5b6c7d8e9f");

    private sealed class FakeStorage : IPhotoStorage
    {
        public Uri BlobEndpoint => new("https://stkochbuch.blob.core.windows.net");

        public Task<UploadSlot> NewUploadSlotAsync(CancellationToken cancellationToken) => Task.FromResult(
            new UploadSlot(Photo, new Uri($"https://stkochbuch.blob.core.windows.net/uploads/{Photo}?sig=x"), DateTimeOffset.UnixEpoch, 1));

        public Task<Stream?> OpenRenditionAsync(Guid photo, int width, CancellationToken cancellationToken) =>
            Task.FromResult<Stream?>(photo == Photo ? new MemoryStream("RIFF....WEBP"u8.ToArray()) : null);
    }

    private HttpClient Gateway() => factory.WithWebHostBuilder(host =>
    {
        host.UseSetting("Koch:Members", Member);
        host.UseSetting("Koch:EasyAuth", "true");
        host.UseSetting("Services:recipes", "http://kochbuch-recipes");
        host.UseSetting("Services:shopping", "http://kochbuch-shopping");
        host.UseSetting("Photos:BlobServiceUri", "https://stkochbuch.blob.core.windows.net/");
        host.ConfigureTestServices(services => services.AddSingleton<IPhotoStorage>(new FakeStorage()));
    }).CreateClient();

    private static async Task<HttpResponseMessage> Send(HttpClient gateway, HttpMethod method, string path, string? user)
    {
        using var request = new HttpRequestMessage(method, new Uri(path, UriKind.Relative));
        if (user is not null) request.Headers.Add("X-MS-CLIENT-PRINCIPAL-ID", user);
        return await gateway.SendAsync(request);
    }

    [Fact]
    public async Task A_member_gets_room_for_one_upload()
    {
        var response = await Send(Gateway(), HttpMethod.Post, "/api/photos/upload-slot", Member);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(Photo, (await response.Content.ReadFromJsonAsync(GatewayJson.Default.UploadSlot))!.Photo);
    }

    [Theory]
    [InlineData(null, HttpStatusCode.Unauthorized)]
    [InlineData("stranger", HttpStatusCode.Forbidden)]
    public async Task Nobody_else_gets_room_or_sees_a_photo(string? user, HttpStatusCode expected)
    {
        Assert.Equal(expected, (await Send(Gateway(), HttpMethod.Post, "/api/photos/upload-slot", user)).StatusCode);
        Assert.Equal(expected, (await Send(Gateway(), HttpMethod.Get, $"/photos/{Photo}/1200.webp", user)).StatusCode);
    }

    [Fact]
    public async Task A_rendition_is_served_as_webp_and_may_be_kept_forever()
    {
        var response = await Send(Gateway(), HttpMethod.Get, $"/photos/{Photo}/1200.webp", Member);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("image/webp", response.Content.Headers.ContentType?.MediaType);
        var cache = response.Headers.CacheControl!;
        Assert.True(cache.Private && cache.MaxAge == TimeSpan.FromDays(365));
        Assert.Contains(cache.Extensions, e => e.Name == "immutable");
    }

    [Theory]
    [InlineData("1200")]
    [InlineData("999")]
    public async Task A_photo_not_processed_yet_or_a_width_that_does_not_exist_is_not_found(string width)
    {
        var photo = width == "999" ? Photo : Guid.NewGuid();

        var response = await Send(Gateway(), HttpMethod.Get, $"/photos/{photo}/{width}.webp", Member);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Uploads_may_go_to_blob_storage_and_nowhere_else()
    {
        var response = await Send(Gateway(), HttpMethod.Get, "/healthz", null);

        var csp = response.Headers.GetValues("Content-Security-Policy").Single();
        Assert.Contains("connect-src 'self' https://stkochbuch.blob.core.windows.net;", csp);
        Assert.Contains("img-src 'self' data:;", csp);
    }
}
