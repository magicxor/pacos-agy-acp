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

        using (Assert.EnterMultipleScope())
        {
            Assert.That(targetWidth, Is.EqualTo(expectedWidth));
            Assert.That(targetHeight, Is.EqualTo(expectedHeight));
        }
    }

    [Test]
    public void FitWithinBounds_WhenImageExceedsMaxDimension_ShouldDownscaleAndEncodeAsJpeg()
    {
        var file = new OutputFile("huge.png", CreateImage(4000, 3000, SKEncodedImageFormat.Png));

        var result = CreateDownscaler().FitWithinBounds(file);
        var (format, width, height) = Probe(result.Content);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.FileName, Is.EqualTo("huge.jpg"));
            Assert.That(format, Is.EqualTo(SKEncodedImageFormat.Jpeg));
            Assert.That(width, Is.EqualTo(2560));
            Assert.That(height, Is.EqualTo(1920));
        }
    }

    [Test]
    public void FitWithinBounds_WhenPortraitImageExceedsMaxDimension_ShouldPreserveAspectRatio()
    {
        var file = new OutputFile("tower.webp", CreateImage(3000, 6000, SKEncodedImageFormat.Webp));

        var result = CreateDownscaler().FitWithinBounds(file);
        var (format, width, height) = Probe(result.Content);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.FileName, Is.EqualTo("tower.jpg"));
            Assert.That(format, Is.EqualTo(SKEncodedImageFormat.Jpeg));
            Assert.That(width, Is.EqualTo(1280));
            Assert.That(height, Is.EqualTo(2560));
        }
    }

    [Test]
    public void FitWithinBounds_WhenShrinkingByMoreThanTwo_ShouldDownscaleWithoutAliasing()
    {
        // Alternating 1px black/white columns average to a flat mid-grey when properly
        // filtered; a resampler that skips source pixels produces moiré instead.
        var file = new OutputFile("stripes.png", CreateStripedImage(6400, 640));

        var result = CreateDownscaler().FitWithinBounds(file);
        using var bitmap = SKBitmap.Decode(result.Content);
        var rows = new[] { 0, bitmap.Height / 2, bitmap.Height - 1, };
        var samples = rows
            .SelectMany(y => Enumerable.Range(0, bitmap.Width).Select(x => (int)bitmap.GetPixel(x, y).Red))
            .ToList();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(bitmap.Width, Is.EqualTo(2560));
            Assert.That(bitmap.Height, Is.EqualTo(256));
            Assert.That(samples, Has.All.InRange(112, 144));
        }
    }

    [Test]
    public void FitWithinBounds_WhenImageWithinMaxDimension_ShouldNotUpscale()
    {
        var file = new OutputFile("small.png", CreateImage(800, 600, SKEncodedImageFormat.Png));

        var result = CreateDownscaler().FitWithinBounds(file);
        var (_, width, height) = Probe(result.Content);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(width, Is.EqualTo(800));
            Assert.That(height, Is.EqualTo(600));
        }
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

    [TestCase(2560, 2560, 2560, false)]
    [TestCase(2561, 100, 2560, true)]
    [TestCase(100, 2561, 2560, true)]
    [TestCase(1000, 800, 2560, false)]
    [TestCase(0, 3000, 2560, false)]
    [TestCase(3000, 0, 2560, false)]
    [TestCase(-5, 3000, 2560, false)]
    [TestCase(int.MaxValue, 1, 2560, true)]
    public void ExceedsMaxDimension_ShouldDetectImagesWithALongerSideAboveTheLimit(
        int width,
        int height,
        int maxDimension,
        bool expected)
    {
        Assert.That(ImageDownscaler.ExceedsMaxDimension(width, height, maxDimension), Is.EqualTo(expected));
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

        return Encode(bitmap, format);
    }

    private static byte[] CreateStripedImage(int width, int height)
    {
        using var bitmap = new SKBitmap(width, height);
        bitmap.Erase(SKColors.White);

        using var canvas = new SKCanvas(bitmap);
        using var paint = new SKPaint { Color = SKColors.Black, };
        for (var x = 0; x < width; x += 2)
        {
            canvas.DrawRect(x, 0, 1, height, paint);
        }

        return Encode(bitmap, SKEncodedImageFormat.Png);
    }

    private static byte[] Encode(SKBitmap bitmap, SKEncodedImageFormat format)
    {
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
