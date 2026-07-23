using Microsoft.Extensions.Logging.Abstractions;
using Pacos.Models;
using Pacos.Services.ImageConversion;
using SkiaSharp;

namespace Pacos.Tests.Unit;

[TestFixture]
[Parallelizable(scope: ParallelScope.All)]
internal sealed class ImageDownscalerTests
{
    private const int MaxDimension = 2560;

    [TestCase(5000, 3000, 2560, 1536)]
    [TestCase(3000, 5000, 1536, 2560)]
    [TestCase(4000, 4000, 2560, 2560)]
    [TestCase(1000, 800, 1000, 800)]
    [TestCase(2560, 2560, 2560, 2560)]
    [TestCase(5120, 2560, 2560, 1280)]
    [TestCase(10000, 1, 2560, 1)]
    public void ComputeTargetDimensions_ShouldFitInsideSquareWithoutUpscaling(
        int width,
        int height,
        int expectedWidth,
        int expectedHeight)
    {
        var (targetWidth, targetHeight) = ImageDownscaler.ComputeTargetDimensions(width, height, MaxDimension);

        Assert.Multiple(() =>
        {
            Assert.That(targetWidth, Is.EqualTo(expectedWidth));
            Assert.That(targetHeight, Is.EqualTo(expectedHeight));
        });
    }

    [Test]
    public void FitWithinBounds_WhenImageExceedsMaxDimension_ShouldDownscaleAndEncodeAsJpeg()
    {
        var file = new OutputFile("huge.png", CreateImage(4000, 3000, SKEncodedImageFormat.Png));

        var result = CreateDownscaler().FitWithinBounds(file);
        var (format, width, height) = Probe(result.Content);

        Assert.Multiple(() =>
        {
            Assert.That(result.FileName, Is.EqualTo("huge.jpg"));
            Assert.That(format, Is.EqualTo(SKEncodedImageFormat.Jpeg));
            Assert.That(width, Is.EqualTo(2560));
            Assert.That(height, Is.EqualTo(1920));
        });
    }

    [Test]
    public void FitWithinBounds_WhenPortraitImageExceedsMaxDimension_ShouldPreserveAspectRatio()
    {
        var file = new OutputFile("tower.webp", CreateImage(3000, 6000, SKEncodedImageFormat.Webp));

        var result = CreateDownscaler().FitWithinBounds(file);
        var (format, width, height) = Probe(result.Content);

        Assert.Multiple(() =>
        {
            Assert.That(result.FileName, Is.EqualTo("tower.jpg"));
            Assert.That(format, Is.EqualTo(SKEncodedImageFormat.Jpeg));
            Assert.That(width, Is.EqualTo(1280));
            Assert.That(height, Is.EqualTo(2560));
        });
    }

    [Test]
    public void FitWithinBounds_WhenImageWithinMaxDimension_ShouldNotUpscale()
    {
        var file = new OutputFile("small.png", CreateImage(800, 600, SKEncodedImageFormat.Png));

        var result = CreateDownscaler().FitWithinBounds(file);
        var (_, width, height) = Probe(result.Content);

        Assert.Multiple(() =>
        {
            Assert.That(width, Is.EqualTo(800));
            Assert.That(height, Is.EqualTo(600));
        });
    }

    [Test]
    public void FitWithinBounds_WhenBytesAreNotAnImage_ShouldReturnOriginalUnchanged()
    {
        var file = new OutputFile("broken.png", [1, 2, 3, 4]);

        var result = CreateDownscaler().FitWithinBounds(file);

        Assert.That(result, Is.SameAs(file));
    }

    [TestCase(2000, 100, 20, false)]
    [TestCase(2001, 100, 20, true)]
    [TestCase(100, 2000, 20, false)]
    [TestCase(100, 2001, 20, true)]
    [TestCase(2000, 2000, 20, false)]
    [TestCase(0, 100, 20, false)]
    [TestCase(100, 0, 20, false)]
    [TestCase(-5, 100, 20, false)]
    [TestCase(int.MaxValue, 1, 20, true)]
    public void ExceedsAspectRatioLimit_ShouldDetectOverlyElongatedImages(
        int width,
        int height,
        int maxAspectRatio,
        bool expected)
    {
        Assert.That(ImageDownscaler.ExceedsAspectRatioLimit(width, height, maxAspectRatio), Is.EqualTo(expected));
    }

    [TestCase(5000, 5000, 10000, false)]
    [TestCase(5000, 5001, 10000, true)]
    [TestCase(9999, 1, 10000, false)]
    [TestCase(10000, 1, 10000, true)]
    [TestCase(0, 0, 10000, false)]
    [TestCase(int.MaxValue, int.MaxValue, 10000, true)]
    public void ExceedsSemiperimeter_ShouldDetectImagesAboveTheLimit(
        int width,
        int height,
        int maxSemiperimeter,
        bool expected)
    {
        Assert.That(ImageDownscaler.ExceedsSemiperimeter(width, height, maxSemiperimeter), Is.EqualTo(expected));
    }

    [TestCase(SKEncodedImageFormat.Png)]
    [TestCase(SKEncodedImageFormat.Webp)]
    public void TryGetDimensions_WhenBytesAreAValidImage_ShouldReturnItsDimensions(SKEncodedImageFormat format)
    {
        var file = new OutputFile("pic", CreateImage(640, 480, format));

        var dimensions = CreateDownscaler().TryGetDimensions(file);

        Assert.That(dimensions, Is.EqualTo((640, 480)));
    }

    [Test]
    public void TryGetDimensions_WhenBytesAreNotAnImage_ShouldReturnNull()
    {
        var file = new OutputFile("broken.png", [1, 2, 3, 4]);

        var dimensions = CreateDownscaler().TryGetDimensions(file);

        Assert.That(dimensions, Is.Null);
    }

    private static ImageDownscaler CreateDownscaler() => new(NullLogger<ImageDownscaler>.Instance);

    private static byte[] CreateImage(int width, int height, SKEncodedImageFormat format)
    {
        using var bitmap = new SKBitmap(width, height);
        bitmap.Erase(SKColors.CornflowerBlue);

        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(format, 100);
        if (data is null)
        {
            throw new InvalidOperationException("Failed to encode the test image.");
        }

        return data.ToArray();
    }

    private static (SKEncodedImageFormat Format, int Width, int Height) Probe(byte[] bytes)
    {
        using var data = SKData.CreateCopy(bytes);
        using var codec = SKCodec.Create(data);

        return (codec.EncodedFormat, codec.Info.Width, codec.Info.Height);
    }
}
