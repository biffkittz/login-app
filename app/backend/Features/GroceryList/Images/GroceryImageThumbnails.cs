using SkiaSharp;

namespace LoginApp.Api.Features.GroceryList.Images;

/// <summary>
/// Shrinks a picture as Gemini draws it (a 1024-pixel PNG of a megabyte or more) to the small square WebP the app
/// stores and serves: a few kilobytes, so a long list loads quickly on a phone.
/// </summary>
internal static class GroceryImageThumbnails
{
    /// <summary>
    /// The width and height of a stored picture, in pixels. The list shows pictures 40 CSS pixels wide, and phones
    /// draw up to three device pixels per CSS pixel: 128 stays sharp on all of them.
    /// </summary>
    public const int PixelSize = 128;

    /// <summary>The media type of stored pictures. WebP is about a third smaller than JPEG at the same quality, and
    /// every browser the app supports shows it.</summary>
    public const string ContentType = "image/webp";

    // Lossy WebP at this quality shows no artifacts at this size.
    private const float WebpQuality = 80;

    // Gemini draws up to 4096 pixels wide. A larger image isn't one it drew, and decoding it could take gigabytes.
    private const int MaxSourceDimension = 4096;

    // Fills transparent areas, so the stored WebP needs no alpha channel (the app's lightest cream).
    private static readonly SKColor s_background = new(0xFD, 0xF7, 0xEE);

    /// <summary>
    /// Center-crops a picture to a square, scales it to <see cref="PixelSize"/> pixels and encodes it as WebP.
    /// </summary>
    /// <param name="encoded">The picture in any format Skia reads (PNG, JPEG, WebP, ...).</param>
    /// <returns>The WebP-encoded thumbnail.</returns>
    /// <exception cref="ImageGenerationException">The bytes aren't a picture, or one of an implausible size.</exception>
    public static byte[] Create(byte[] encoded)
    {
        ArgumentNullException.ThrowIfNull(encoded);

        using var data = SKData.CreateCopy(encoded);
        using SKCodec codec = SKCodec.Create(data) ?? throw new ImageGenerationException("The picture couldn't be read");

        // Checked before decoding, from the header alone.
        SKImageInfo info = codec.Info;
        if (info.Width <= 0 || info.Height <= 0 || info.Width > MaxSourceDimension || info.Height > MaxSourceDimension)
        {
            throw new ImageGenerationException($"The picture was {info.Width}×{info.Height} pixels");
        }

        using SKBitmap source = SKBitmap.Decode(codec, new SKImageInfo(info.Width, info.Height, SKColorType.Rgba8888, SKAlphaType.Premul))
            ?? throw new ImageGenerationException("The picture couldn't be decoded");

        // Gemini is asked for a square, but nothing forces it to draw one.
        int side = Math.Min(source.Width, source.Height);
        using SKBitmap square = new();
        if (!source.ExtractSubset(square, SKRectI.Create((source.Width - side) / 2, (source.Height - side) / 2, side, side)))
        {
            throw new ImageGenerationException("The picture couldn't be cropped");
        }

        using SKBitmap scaled = ScaleDown(square, PixelSize);
        using SKBitmap opaque = new(new SKImageInfo(PixelSize, PixelSize, SKColorType.Rgba8888, SKAlphaType.Opaque));
        using (SKCanvas canvas = new(opaque))
        {
            canvas.Clear(s_background);
            canvas.DrawBitmap(scaled, 0, 0, SKSamplingOptions.Default, paint: null);
        }

        using SKPixmap pixels = opaque.PeekPixels();
        using SKData webp = pixels.Encode(new SKWebpEncoderOptions(SKWebpEncoderCompression.Lossy, WebpQuality))
            ?? throw new ImageGenerationException("The picture couldn't be encoded");
        return webp.ToArray();
    }

    // Shrinking 1024 pixels to 128 in one step would sample only a few of every 64 source pixels and turn fine detail
    // into noise. Halving with bilinear filtering averages each 2×2 block instead, so the picture is halved until the
    // last step is less than half, which a cubic filter does well.
    private static SKBitmap ScaleDown(SKBitmap source, int size)
    {
        SKBitmap current = source.Copy() ?? throw new ImageGenerationException("The picture couldn't be copied");
        while (current.Width / 2 >= size)
        {
            SKBitmap half = Resize(current, current.Width / 2, new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.None));
            current.Dispose();
            current = half;
        }

        if (current.Width == size)
        {
            return current;
        }

        SKBitmap final = Resize(current, size, new SKSamplingOptions(SKCubicResampler.Mitchell));
        current.Dispose();
        return final;
    }

    private static SKBitmap Resize(SKBitmap bitmap, int size, SKSamplingOptions sampling) =>
        bitmap.Resize(new SKImageInfo(size, size, bitmap.ColorType, bitmap.AlphaType), sampling)
        ?? throw new ImageGenerationException("The picture couldn't be resized");
}
