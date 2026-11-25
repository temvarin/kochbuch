using Koch.Contracts;
using Koch.Media.Uploads;
using Microsoft.Extensions.Logging.Abstractions;
using SkiaSharp;

namespace Koch.Media.Tests;

public class RenditionsTests
{
    private static SKImageInfo Info(byte[] webp)
    {
        using var codec = SKCodec.Create(new MemoryStream(webp));
        Assert.Equal(SKEncodedImageFormat.Webp, codec.EncodedFormat);
        return codec.Info;
    }

    [Fact]
    public void A_phone_photo_becomes_one_webp_per_width_keeping_its_proportions()
    {
        var renditions = Renditions.Make(Photos.Jpeg(3000, 2000));

        Assert.Equal(PhotoPaths.Widths, renditions.Select(r => r.Width));
        Assert.All(renditions, r =>
        {
            var info = Info(r.Webp);
            Assert.Equal(r.Width, info.Width);
            Assert.Equal((int)Math.Round(r.Width * 2 / 3.0), info.Height);
        });
    }

    [Fact]
    public void Nothing_of_the_cameras_metadata_survives_gps_included()
    {
        var upload = Photos.Jpeg(1600, 1200, orientation: 1, withGps: true);
        Assert.True(Photos.Contains(upload, Photos.Secret));
        Assert.True(Photos.Contains(upload, "Exif"));

        foreach (var rendition in Renditions.Make(upload))
        {
            Assert.False(Photos.Contains(rendition.Webp, Photos.Secret));
            Assert.False(Photos.Contains(rendition.Webp, "EXIF"));
            Assert.False(Photos.Contains(rendition.Webp, "Exif"));
        }
    }

    [Fact]
    public void A_photo_taken_sideways_comes_out_upright()
    {
        var rendition = Renditions.Make(Photos.Jpeg(1600, 1200, orientation: 6))[^1];

        var info = Info(rendition.Webp);
        Assert.True(info.Height > info.Width, $"{info.Width}x{info.Height} is not portrait");
    }

    [Fact]
    public void A_small_photo_is_not_blown_up()
    {
        var renditions = Renditions.Make(Photos.Jpeg(300, 200));

        Assert.All(renditions, r => Assert.Equal(300, Info(r.Webp).Width));
    }

    [Fact]
    public void Png_is_accepted_too()
    {
        Assert.Equal(2, Renditions.Make(Photos.Png(800, 600)).Count);
    }

    public static TheoryData<byte[]> NotPhotos => new()
    {
        "not an image at all"u8.ToArray(),
        Photos.HugePngHeader(20_000, 20_000),
    };

    [Theory, MemberData(nameof(NotPhotos))]
    public void What_is_not_a_usable_photo_is_refused(byte[] upload)
    {
        Assert.Throws<NotAPhotoException>(() => Renditions.Make(upload));
    }

    [Fact]
    public void An_upload_over_the_limit_is_refused()
    {
        Assert.Throws<NotAPhotoException>(() => Renditions.Make(new byte[PhotoPaths.MaxUploadBytes + 1]));
    }
}

public class PhotoProcessorTests
{
    private static readonly Guid Id = new("3c9e8f10-5a1b-4c2d-8e3f-4a5b6c7d8e9f");

    private static PhotoProcessor Processor(MemoryBlobs blobs) =>
        new(blobs, NullLogger<PhotoProcessor>.Instance);

    [Fact]
    public async Task An_upload_becomes_renditions_and_is_deleted_only_after_they_are_written()
    {
        var blobs = new MemoryBlobs();
        blobs.Uploads[Id] = Photos.Jpeg(1600, 1200);

        Assert.Equal(Outcome.Done, await Processor(blobs).ProcessAsync(Id, CancellationToken.None));

        Assert.Equal(["write 400", "write 1200", "delete"], blobs.Log);
        Assert.Empty(blobs.Uploads);
    }

    [Fact]
    public async Task The_same_message_delivered_twice_is_done_once()
    {
        var blobs = new MemoryBlobs();
        blobs.Uploads[Id] = Photos.Jpeg(1600, 1200);
        await Processor(blobs).ProcessAsync(Id, CancellationToken.None);

        Assert.Equal(Outcome.AlreadyDone, await Processor(blobs).ProcessAsync(Id, CancellationToken.None));
        Assert.Equal(2, blobs.Renditions.Count);
    }

    [Fact]
    public async Task What_is_not_a_photo_is_dropped_and_not_retried()
    {
        var blobs = new MemoryBlobs();
        blobs.Uploads[Id] = "<script>alert(1)</script>"u8.ToArray();

        Assert.Equal(Outcome.Refused, await Processor(blobs).ProcessAsync(Id, CancellationToken.None));
        Assert.Empty(blobs.Uploads);
        Assert.Empty(blobs.Renditions);
    }

    [Fact]
    public async Task An_upload_that_is_gone_without_renditions_is_reported_missing()
    {
        Assert.Equal(Outcome.Missing, await Processor(new MemoryBlobs()).ProcessAsync(Id, CancellationToken.None));
    }
}

public class BlobCreatedTests
{
    private const string Id = "3c9e8f10-5a1b-4c2d-8e3f-4a5b6c7d8e9f";

    private static string Event(string subject, string type = "Microsoft.Storage.BlobCreated") =>
        $$"""{"id":"1","eventType":"{{type}}","subject":"{{subject}}","data":{"contentLength":1234},"dataVersion":"","metadataVersion":"1","eventTime":"2026-11-22T18:00:00Z","topic":"/subscriptions/x"}""";

    [Fact]
    public void A_blob_created_in_uploads_names_the_photo()
    {
        Assert.True(BlobCreated.TryParse(Event($"/blobServices/default/containers/uploads/blobs/{Id}"), out var id));
        Assert.Equal(new Guid(Id), id);
    }

    [Fact]
    public void An_array_of_one_event_is_read_too()
    {
        Assert.True(BlobCreated.TryParse($"[{Event($"/blobServices/default/containers/uploads/blobs/{Id}")}]", out _));
    }

    [Theory]
    [InlineData($"/blobServices/default/containers/photos/blobs/{Id}/400.webp", "Microsoft.Storage.BlobCreated")]
    [InlineData($"/blobServices/default/containers/uploads/blobs/{Id}", "Microsoft.Storage.BlobDeleted")]
    [InlineData("/blobServices/default/containers/uploads/blobs/../photos/x", "Microsoft.Storage.BlobCreated")]
    [InlineData("/blobServices/default/containers/uploads/blobs/not-a-guid", "Microsoft.Storage.BlobCreated")]
    public void Anything_else_is_not_an_upload(string subject, string type)
    {
        Assert.False(BlobCreated.TryParse(Event(subject, type), out _));
    }

    [Theory]
    [InlineData("")]
    [InlineData("{")]
    [InlineData("42")]
    [InlineData("[]")]
    public void Garbage_is_not_an_upload(string message)
    {
        Assert.False(BlobCreated.TryParse(message, out _));
    }
}
