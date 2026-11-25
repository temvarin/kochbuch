using System.Buffers.Binary;
using System.Text;
using SkiaSharp;

namespace Koch.Media.Tests;

/// <summary>Test photos made on the spot, with the metadata a phone camera writes.</summary>
public static class Photos
{
    /// <summary>A text placed in the EXIF data (description and GPS section) that must never reach a rendition.</summary>
    public const string Secret = "SECRET-KITCHEN-48.137N-11.575E";

    public static byte[] Jpeg(int width, int height, ushort? orientation = null, bool withGps = false)
    {
        using var bitmap = new SKBitmap(width, height);
        using (var canvas = new SKCanvas(bitmap))
        {
            canvas.Clear(SKColors.Goldenrod);
            // A dark band at the top: shows which way up the result is.
            using var paint = new SKPaint { Color = SKColors.Black };
            canvas.DrawRect(0, 0, width, height / 4f, paint);
        }
        using var image = SKImage.FromBitmap(bitmap);
        var jpeg = image.Encode(SKEncodedImageFormat.Jpeg, 90).ToArray();
        return orientation is null && !withGps ? jpeg : WithExif(jpeg, orientation ?? 1, withGps);
    }

    public static byte[] Png(int width, int height)
    {
        using var bitmap = new SKBitmap(width, height);
        using var image = SKImage.FromBitmap(bitmap);
        return image.Encode(SKEncodedImageFormat.Png, 100).ToArray();
    }

    /// <summary>A PNG header that claims a huge image, with no pixel data: a decompression bomb's announcement.</summary>
    public static byte[] HugePngHeader(uint width, uint height)
    {
        var ihdr = new byte[13];
        BinaryPrimitives.WriteUInt32BigEndian(ihdr, width);
        BinaryPrimitives.WriteUInt32BigEndian(ihdr.AsSpan(4), height);
        ihdr[8] = 8; // bit depth
        ihdr[9] = 2; // RGB
        return [0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A, .. Chunk("IHDR", ihdr), .. Chunk("IEND", [])];
    }

    public static bool Contains(byte[] haystack, string text) =>
        haystack.AsSpan().IndexOf(Encoding.ASCII.GetBytes(text)) >= 0;

    /// <summary>
    /// Inserts an APP1 EXIF segment right after the JPEG's start: orientation, an image description and, if asked, a GPS
    /// section, all big-endian TIFF.
    /// </summary>
    private static byte[] WithExif(byte[] jpeg, ushort orientation, bool withGps)
    {
        var secret = Encoding.ASCII.GetBytes(Secret + "\0");
        var entries = withGps ? 3 : 2;
        var ifd0Size = 2 + entries * 12 + 4;
        var gpsOffset = 8 + ifd0Size;
        var gpsSize = withGps ? 2 + 12 + 4 : 0;
        var secretOffset = gpsOffset + gpsSize;
        var tiff = new byte[secretOffset + secret.Length];
        "MM"u8.CopyTo(tiff);
        BinaryPrimitives.WriteUInt16BigEndian(tiff.AsSpan(2), 42);
        BinaryPrimitives.WriteUInt32BigEndian(tiff.AsSpan(4), 8);
        var at = 8;
        BinaryPrimitives.WriteUInt16BigEndian(tiff.AsSpan(at), (ushort)entries);
        at += 2;
        at = Entry(tiff, at, 0x010E, 2, (uint)secret.Length, (uint)secretOffset); // ImageDescription
        at = Entry(tiff, at, 0x0112, 3, 1, (uint)orientation << 16); // Orientation, SHORT, left-justified
        if (withGps) at = Entry(tiff, at, 0x8825, 4, 1, (uint)gpsOffset); // GPS IFD pointer
        BinaryPrimitives.WriteUInt32BigEndian(tiff.AsSpan(at), 0);
        if (withGps)
        {
            at = gpsOffset;
            BinaryPrimitives.WriteUInt16BigEndian(tiff.AsSpan(at), 1);
            at = Entry(tiff, at + 2, 0x001B, 7, (uint)secret.Length, (uint)secretOffset); // GPSProcessingMethod
            BinaryPrimitives.WriteUInt32BigEndian(tiff.AsSpan(at), 0);
        }
        secret.CopyTo(tiff, secretOffset);

        byte[] payload = [.. "Exif\0\0"u8, .. tiff];
        var length = (ushort)(payload.Length + 2);
        return [jpeg[0], jpeg[1], 0xFF, 0xE1, (byte)(length >> 8), (byte)length, .. payload, .. jpeg.AsSpan(2)];
    }

    private static int Entry(byte[] tiff, int at, ushort tag, ushort type, uint count, uint value)
    {
        BinaryPrimitives.WriteUInt16BigEndian(tiff.AsSpan(at), tag);
        BinaryPrimitives.WriteUInt16BigEndian(tiff.AsSpan(at + 2), type);
        BinaryPrimitives.WriteUInt32BigEndian(tiff.AsSpan(at + 4), count);
        BinaryPrimitives.WriteUInt32BigEndian(tiff.AsSpan(at + 8), value);
        return at + 12;
    }

    private static byte[] Chunk(string type, byte[] data)
    {
        byte[] typeAndData = [.. Encoding.ASCII.GetBytes(type), .. data];
        var chunk = new byte[4 + typeAndData.Length + 4];
        BinaryPrimitives.WriteUInt32BigEndian(chunk, (uint)data.Length);
        typeAndData.CopyTo(chunk, 4);
        BinaryPrimitives.WriteUInt32BigEndian(chunk.AsSpan(4 + typeAndData.Length), System.IO.Hashing.Crc32.HashToUInt32(typeAndData));
        return chunk;
    }
}
