using LoginApp.Api.Features.GroceryList.Images;
using SkiaSharp;

namespace LoginApp.Api.Tests.Features.GroceryList.Images;

/// <summary>
/// How pictures from Gemini are shrunk for storage: small, square, WebP, without an alpha channel.
/// </summary>
public sealed class GroceryImageThumbnailsTests
{
    [Fact]
    public void Create_FromAPictureAsGeminiDrawsIt_ReturnsASmallSquareWebp()
    {
        // Arrange
        byte[] original = TestPictures.Png(1024, 1024);

        // Act
        byte[] thumbnail = GroceryImageThumbnails.Create(original);

        // Assert
        (SKEncodedImageFormat format, SKAlphaType alphaType) = TestPictures.Describe(thumbnail);
        Assert.Equal(SKEncodedImageFormat.Webp, format);
        Assert.Equal(SKAlphaType.Opaque, alphaType);
        using SKBitmap bitmap = TestPictures.Decode(thumbnail);
        Assert.Equal((128, 128), (bitmap.Width, bitmap.Height));
        Assert.True(thumbnail.Length < 8 * 1024, $"The thumbnail is {thumbnail.Length} bytes; it should be a few kilobytes.");
        Assert.True(original.Length > 10 * thumbnail.Length);
    }

    [Fact]
    public void Create_KeepsWhatThePictureShows()
    {
        // Arrange: an orange circle in the middle of a cream background.
        byte[] original = TestPictures.Png(1024, 1024);

        // Act
        using SKBitmap thumbnail = TestPictures.Decode(GroceryImageThumbnails.Create(original));

        // Assert
        AssertClose(new SKColor(0xE0, 0x74, 0x2C), thumbnail.GetPixel(64, 80));
        AssertClose(new SKColor(0xF8, 0xEC, 0xD9), thumbnail.GetPixel(4, 4));
    }

    [Fact]
    public void Create_FromAWidePicture_KeepsItsMiddle()
    {
        // Arrange: red | blue | green, each a third of a 3:1 picture, so the middle square is all blue.
        byte[] original = TestPictures.Stripes(900, 300, SKColors.Red, SKColors.Blue, SKColors.Green);

        // Act
        using SKBitmap thumbnail = TestPictures.Decode(GroceryImageThumbnails.Create(original));

        // Assert
        Assert.Equal((128, 128), (thumbnail.Width, thumbnail.Height));
        AssertClose(SKColors.Blue, thumbnail.GetPixel(2, 64));
        AssertClose(SKColors.Blue, thumbnail.GetPixel(125, 64));
    }

    [Fact]
    public void Create_FromATransparentPicture_FillsTheBackground()
    {
        // Act
        byte[] thumbnail = GroceryImageThumbnails.Create(TestPictures.Transparent(256));

        // Assert
        Assert.Equal(SKAlphaType.Opaque, TestPictures.Describe(thumbnail).AlphaType);
        using SKBitmap bitmap = TestPictures.Decode(thumbnail);
        AssertClose(new SKColor(0xFD, 0xF7, 0xEE), bitmap.GetPixel(64, 64));
    }

    [Fact]
    public void Create_FromASmallPicture_ScalesItUp()
    {
        // Act
        using SKBitmap thumbnail = TestPictures.Decode(GroceryImageThumbnails.Create(TestPictures.Png(64, 64)));

        // Assert
        Assert.Equal((128, 128), (thumbnail.Width, thumbnail.Height));
    }

    [Fact]
    public void Create_FromSomethingThatIsNotAPicture_Throws()
    {
        // Arrange
        byte[] notAPicture = "<html>Service unavailable</html>"u8.ToArray();

        // Act + Assert
        Assert.Throws<ImageGenerationException>(() => GroceryImageThumbnails.Create(notAPicture));
    }

    [Theory]
    [InlineData(20_000, 20_000)]
    [InlineData(4_097, 1_024)]
    public void Create_FromAPictureOfImplausibleSize_ThrowsBeforeDecodingIt(int width, int height)
    {
        // Arrange: a header that promises gigabytes of pixels.
        byte[] bomb = TestPictures.PngHeaderOnly(width, height);

        // Act
        ImageGenerationException exception = Assert.Throws<ImageGenerationException>(() => GroceryImageThumbnails.Create(bomb));

        // Assert
        Assert.Contains($"{width}×{height}", exception.Message, StringComparison.Ordinal);
    }

    // Lossy WebP shifts colors a little; "close" is within a few steps per channel.
    private static void AssertClose(SKColor expected, SKColor actual)
    {
        const int Tolerance = 12;
        Assert.True(
            Math.Abs(expected.Red - actual.Red) <= Tolerance
                && Math.Abs(expected.Green - actual.Green) <= Tolerance
                && Math.Abs(expected.Blue - actual.Blue) <= Tolerance,
            $"Expected about {expected}, but was {actual}.");
    }
}
