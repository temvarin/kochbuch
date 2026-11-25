using Koch.Contracts;
using SkiaSharp;

namespace Koch.Media.Uploads;

public sealed record Rendition(int Width, int Height, byte[] Webp);

public sealed class NotAPhotoException(string message) : Exception(message);

/// <summary>
/// Turns an uploaded photo into the renditions of <see cref="PhotoPaths.Widths"/>. Pixels are decoded, rotated
/// upright according to EXIF, and re-encoded as WebP so no camera/GPS metadata survives.
/// </summary>
public static class Renditions
{
    private static readonly SKEncodedImageFormat[] Accepted = [SKEncodedImageFormat.Jpeg, SKEncodedImageFormat.Png, SKEncodedImageFormat.Webp];

    public const int MaxPixels = 50_000_000;
    public const int Quality = 80;

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
        canvas.DrawImage(image, 0, 0, SKSamplingOptions.Default);
        return result;
    }
}
