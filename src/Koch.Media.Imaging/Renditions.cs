using Koch.Media.Contracts;
using SkiaSharp;

namespace Koch.Media.Imaging;

/// <summary>A photo at one width, as WebP.</summary>
public sealed record Rendition(int Width, int Height, byte[] Webp);

/// <summary>The upload is not a photo Media can use; it is dropped, never retried.</summary>
public sealed class NotAPhotoException(string message) : Exception(message);

/// <summary>
/// Turns an uploaded photo into the renditions of <see cref="PhotoPaths.Widths"/>. The pixels are decoded, turned upright
/// and encoded anew, so nothing of the original file survives: no EXIF, no GPS position of the kitchen, no camera data
/// (docs/adr/0006).
/// </summary>
public static class Renditions
{
    /// <summary>JPEG, PNG and WebP: what phone cameras and browsers produce.</summary>
    private static readonly SKEncodedImageFormat[] Accepted = [SKEncodedImageFormat.Jpeg, SKEncodedImageFormat.Png, SKEncodedImageFormat.Webp];

    /// <summary>Larger images are refused before decoding, so a small file can't unpack into gigabytes.</summary>
    public const int MaxPixels = 50_000_000;

    public const int Quality = 80;

    /// <exception cref="NotAPhotoException">Not an accepted image, too large, or broken.</exception>
    public static IReadOnlyList<Rendition> Make(byte[] upload)
    {
        if (upload.LongLength > PhotoPaths.MaxUploadBytes) throw new NotAPhotoException("The upload is too large.");
        using var data = SKData.CreateCopy(upload);
        using var codec = SKCodec.Create(data) ?? throw new NotAPhotoException("The upload is not an image.");
        if (!Accepted.Contains(codec.EncodedFormat)) throw new NotAPhotoException($"{codec.EncodedFormat} is not accepted.");
        if ((long)codec.Info.Width * codec.Info.Height > MaxPixels) throw new NotAPhotoException("The image has too many pixels.");

        using var decoded = new SKBitmap(codec.Info.WithColorType(SKColorType.Rgba8888).WithAlphaType(SKAlphaType.Premul));
        var result = codec.GetPixels(decoded.Info, decoded.GetPixels());
        if (result is not (SKCodecResult.Success or SKCodecResult.IncompleteInput))
            throw new NotAPhotoException($"The image can't be decoded: {result}.");
        using var upright = Upright(decoded, codec.EncodedOrigin);

        return [.. PhotoPaths.Widths.Select(width => Encode(upright, width))];
    }

    /// <summary>Never wider than the original: a small photo is not blown up.</summary>
    private static Rendition Encode(SKBitmap source, int width)
    {
        var target = Math.Min(width, source.Width);
        var height = Math.Max(1, (int)Math.Round((double)source.Height * target / source.Width));
        using var resized = target == source.Width
            ? source.Copy()
            : source.Resize(new SKImageInfo(target, height), new SKSamplingOptions(SKCubicResampler.Mitchell));
        using var image = SKImage.FromBitmap(resized);
        using var webp = image.Encode(SKEncodedImageFormat.Webp, Quality)
            ?? throw new InvalidOperationException("WebP encoding failed.");
        return new Rendition(width, height, webp.ToArray());
    }

    /// <summary>
    /// Phones store a photo as the sensor saw it and note in EXIF how to turn it. That note is about to be dropped, so
    /// the turn is applied to the pixels here.
    /// </summary>
    private static SKBitmap Upright(SKBitmap bitmap, SKEncodedOrigin origin)
    {
        var swap = origin is SKEncodedOrigin.LeftTop or SKEncodedOrigin.RightTop or SKEncodedOrigin.RightBottom or SKEncodedOrigin.LeftBottom;
        var (w, h) = swap ? (bitmap.Height, bitmap.Width) : (bitmap.Width, bitmap.Height);
        var result = new SKBitmap(new SKImageInfo(w, h, bitmap.ColorType, bitmap.AlphaType));
        using var canvas = new SKCanvas(result);
        canvas.SetMatrix(origin switch
        {
            SKEncodedOrigin.TopRight => new SKMatrix(-1, 0, w, 0, 1, 0, 0, 0, 1),
            SKEncodedOrigin.BottomRight => new SKMatrix(-1, 0, w, 0, -1, h, 0, 0, 1),
            SKEncodedOrigin.BottomLeft => new SKMatrix(1, 0, 0, 0, -1, h, 0, 0, 1),
            SKEncodedOrigin.LeftTop => new SKMatrix(0, 1, 0, 1, 0, 0, 0, 0, 1),
            SKEncodedOrigin.RightTop => new SKMatrix(0, -1, w, 1, 0, 0, 0, 0, 1),
            SKEncodedOrigin.RightBottom => new SKMatrix(0, -1, w, -1, 0, h, 0, 0, 1),
            SKEncodedOrigin.LeftBottom => new SKMatrix(0, 1, 0, -1, 0, h, 0, 0, 1),
            _ => SKMatrix.Identity,
        });
        using var image = SKImage.FromBitmap(bitmap);
        canvas.DrawImage(image, 0, 0, SKSamplingOptions.Default); // whole pixels moved, nothing resampled
        return result;
    }
}
