using SkiaSharp;

namespace LoginApp.Api.Tests.Features.GroceryList.Images;

/// <summary>
/// Pictures for tests, made with the same library the app uses to read them.
/// </summary>
internal static class TestPictures
{
    /// <summary>
    /// Draws something like what Gemini returns: a PNG with a background, a shape and some detail.
    /// </summary>
    /// <param name="width">The width in pixels.</param>
    /// <param name="height">The height in pixels.</param>
    /// <param name="background">The background color.</param>
    /// <returns>The PNG.</returns>
    public static byte[] Png(int width = 1024, int height = 1024, SKColor? background = null)
    {
        using SKBitmap bitmap = new(width, height, SKColorType.Rgba8888, SKAlphaType.Premul);
        using (SKCanvas canvas = new(bitmap))
        {
            canvas.Clear(background ?? new SKColor(0xF8, 0xEC, 0xD9));
            using SKPaint paint = new() { Color = new SKColor(0xE0, 0x74, 0x2C), IsAntialias = true };
            canvas.DrawCircle(width / 2f, height / 2f, Math.Min(width, height) / 3f, paint);
            paint.Color = new SKColor(0x3F, 0x5B, 0x3A);
            canvas.DrawRect(width * 0.45f, height * 0.1f, width * 0.1f, height * 0.2f, paint);
        }

        return Encode(bitmap);
    }

    /// <summary>
    /// Draws three vertical stripes of equal width, to show which part of a picture survives cropping.
    /// </summary>
    /// <param name="width">The width in pixels.</param>
    /// <param name="height">The height in pixels.</param>
    /// <param name="left">The left stripe's color.</param>
    /// <param name="middle">The middle stripe's color.</param>
    /// <param name="right">The right stripe's color.</param>
    /// <returns>The PNG.</returns>
    public static byte[] Stripes(int width, int height, SKColor left, SKColor middle, SKColor right)
    {
        using SKBitmap bitmap = new(width, height, SKColorType.Rgba8888, SKAlphaType.Premul);
        using (SKCanvas canvas = new(bitmap))
        {
            using SKPaint paint = new();
            float third = width / 3f;
            foreach ((SKColor color, int index) in new[] { left, middle, right }.Select((color, index) => (color, index)))
            {
                paint.Color = color;
                canvas.DrawRect(index * third, 0, third, height, paint);
            }
        }

        return Encode(bitmap);
    }

    /// <summary>Makes a fully transparent PNG.</summary>
    /// <param name="size">The width and height in pixels.</param>
    /// <returns>The PNG.</returns>
    public static byte[] Transparent(int size)
    {
        using SKBitmap bitmap = new(size, size, SKColorType.Rgba8888, SKAlphaType.Premul);
        bitmap.Erase(SKColors.Transparent);
        return Encode(bitmap);
    }

    /// <summary>Reads a picture back.</summary>
    /// <param name="encoded">The encoded picture.</param>
    /// <returns>The decoded bitmap; the caller disposes it.</returns>
    public static SKBitmap Decode(byte[] encoded) =>
        SKBitmap.Decode(encoded) ?? throw new InvalidOperationException("Not a picture.");

    /// <summary>Reads what a picture's header says, without decoding it.</summary>
    /// <param name="encoded">The encoded picture.</param>
    /// <returns>The format and alpha type.</returns>
    public static (SKEncodedImageFormat Format, SKAlphaType AlphaType) Describe(byte[] encoded)
    {
        using var data = SKData.CreateCopy(encoded);
        using SKCodec codec = SKCodec.Create(data) ?? throw new InvalidOperationException("Not a picture.");
        return (codec.EncodedFormat, codec.Info.AlphaType);
    }

    /// <summary>
    /// Makes a PNG whose header claims the given size, with (almost) no pixels behind it: what a decompression bomb
    /// looks like before it is decoded.
    /// </summary>
    /// <param name="width">The claimed width.</param>
    /// <param name="height">The claimed height.</param>
    /// <returns>The bytes.</returns>
    public static byte[] PngHeaderOnly(int width, int height)
    {
        byte[] header = new byte[13];
        System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(0, 4), width);
        System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(4, 4), height);
        header[8] = 8; // bit depth
        header[9] = 6; // RGBA

        using MemoryStream png = new();
        png.Write([0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A]);
        WriteChunk(png, "IHDR", header);

        // Skia reads up to the first image data before it reports a picture's size, so there must be some: a zlib
        // stream of nothing at all.
        WriteChunk(png, "IDAT", [0x78, 0x9C, 0x03, 0x00, 0x00, 0x00, 0x00, 0x01]);
        WriteChunk(png, "IEND", []);
        return png.ToArray();
    }

    private static byte[] Encode(SKBitmap bitmap)
    {
        using SKData data = bitmap.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    private static void WriteChunk(Stream stream, string type, byte[] data)
    {
        byte[] length = new byte[4];
        System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(length, data.Length);
        byte[] typeBytes = System.Text.Encoding.ASCII.GetBytes(type);
        byte[] crc = new byte[4];
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(crc, Crc32([.. typeBytes, .. data]));
        stream.Write(length);
        stream.Write(typeBytes);
        stream.Write(data);
        stream.Write(crc);
    }

    // PNG's chunk checksum (ISO 3309 CRC-32), bit by bit: speed doesn't matter for 17 bytes.
    private static uint Crc32(byte[] bytes)
    {
        uint crc = 0xFFFFFFFF;
        foreach (byte value in bytes)
        {
            crc ^= value;
            for (int bit = 0; bit < 8; bit++)
            {
                crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB88320 : crc >> 1;
            }
        }

        return ~crc;
    }
}
