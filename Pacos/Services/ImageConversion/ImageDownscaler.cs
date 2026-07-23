using Pacos.Models;
using SkiaSharp;

namespace Pacos.Services.ImageConversion;

/// <summary>
/// Shrinks over-sized images so they fit within Telegram's photo upload limit:
/// the picture is scaled to fit inside a <see cref="MaxDimension"/>×<see cref="MaxDimension"/>
/// square (aspect ratio preserved, never upscaled) and re-encoded as JPEG.
/// Re-encoding drops any alpha channel and flattens animated images to their
/// first frame, both acceptable since these files are already delivered as
/// static photos. The transform is best-effort: undecodable input is returned
/// unchanged rather than throwing into the send path.
/// </summary>
public sealed class ImageDownscaler
{
    private const int MaxDimension = 2560;
    private const int JpegQuality = 90;

    private static readonly SKSamplingOptions Sampling = new(SKCubicResampler.Mitchell);

    private readonly ILogger<ImageDownscaler> _logger;

    public ImageDownscaler(ILogger<ImageDownscaler> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Returns a JPEG-encoded copy of <paramref name="file"/> that fits inside the
    /// <see cref="MaxDimension"/> square, or the original file when it cannot be
    /// decoded or encoded.
    /// </summary>
    public OutputFile FitWithinBounds(OutputFile file)
    {
        try
        {
            using var decoded = SKBitmap.Decode(file.Content);
            if (decoded is null)
            {
                _logger.LogWarning("Could not decode {FileName} as an image; sending it unchanged", file.FileName);
                return file;
            }

            var (targetWidth, targetHeight) = ComputeTargetDimensions(decoded.Width, decoded.Height, MaxDimension);

            SKBitmap? resized = null;
            try
            {
                if (targetWidth != decoded.Width || targetHeight != decoded.Height)
                {
                    resized = decoded.Resize(decoded.Info.WithSize(targetWidth, targetHeight), Sampling);
                }

                using var image = SKImage.FromBitmap(resized ?? decoded);
                using var data = image.Encode(SKEncodedImageFormat.Jpeg, JpegQuality);
                if (data is null)
                {
                    _logger.LogWarning("Could not JPEG-encode {FileName}; sending it unchanged", file.FileName);
                    return file;
                }

                var jpegBytes = data.ToArray();
                _logger.LogInformation(
                    "Downscaled {FileName} from {OriginalWidth}x{OriginalHeight} ({OriginalBytes} bytes) to {TargetWidth}x{TargetHeight} JPEG ({JpegBytes} bytes)",
                    file.FileName,
                    decoded.Width,
                    decoded.Height,
                    file.Content.Length,
                    targetWidth,
                    targetHeight,
                    jpegBytes.Length);

                return new OutputFile(Path.ChangeExtension(file.FileName, ".jpg"), jpegBytes);
            }
            finally
            {
                resized?.Dispose();
            }
        }
        catch (Exception e)
        {
            _logger.LogWarning(e, "Failed to downscale {FileName}; sending it unchanged", file.FileName);
            return file;
        }
    }

    /// <summary>
    /// Reads the pixel dimensions from <paramref name="file"/>'s image header without
    /// decoding the full bitmap, or <see langword="null"/> when the bytes cannot be
    /// read as an image. Best-effort: never throws into the send path.
    /// </summary>
    public (int Width, int Height)? TryGetDimensions(OutputFile file)
    {
        try
        {
            using var stream = new MemoryStream(file.Content, writable: false);
            using var codec = SKCodec.Create(stream, out var result);
            if (result != SKCodecResult.Success)
            {
                _logger.LogWarning(
                    "Could not read the dimensions of {FileName} ({Result}); treating it as an ordinary image",
                    file.FileName,
                    result);
                return null;
            }

            return (codec.Info.Width, codec.Info.Height);
        }
        catch (Exception e)
        {
            _logger.LogWarning(e, "Failed to read the dimensions of {FileName}; treating it as an ordinary image", file.FileName);
            return null;
        }
    }

    /// <summary>
    /// Computes the target dimensions that fit a <paramref name="width"/>×<paramref name="height"/>
    /// image inside a <paramref name="maxDimension"/> square while preserving the
    /// aspect ratio and never upscaling. Pure and side-effect free so it can be
    /// tested without decoding an image.
    /// </summary>
    internal static (int Width, int Height) ComputeTargetDimensions(int width, int height, int maxDimension)
    {
        if (width <= 0 || height <= 0)
        {
            return (width, height);
        }

        var scale = Math.Min(1.0, Math.Min((double)maxDimension / width, (double)maxDimension / height));

        var targetWidth = Math.Clamp((int)Math.Round(width * scale), 1, maxDimension);
        var targetHeight = Math.Clamp((int)Math.Round(height * scale), 1, maxDimension);

        return (targetWidth, targetHeight);
    }

    /// <summary>
    /// Returns whether the image is more elongated than Telegram allows for a photo
    /// (its longer side exceeds <paramref name="maxAspectRatio"/>× its shorter side),
    /// in which case it must be sent as a document instead. Pure and side-effect free.
    /// </summary>
    internal static bool ExceedsAspectRatioLimit(int width, int height, int maxAspectRatio) =>
        width > 0
        && height > 0
        && Math.Max(width, height) > (long)Math.Min(width, height) * maxAspectRatio;

    /// <summary>
    /// Returns whether the image's width + height exceeds Telegram's photo
    /// semiperimeter limit, in which case the photo must be downscaled. Pure and
    /// side-effect free.
    /// </summary>
    internal static bool ExceedsSemiperimeter(int width, int height, int maxSemiperimeter) =>
        width > 0
        && height > 0
        && (long)width + height > maxSemiperimeter;
}
