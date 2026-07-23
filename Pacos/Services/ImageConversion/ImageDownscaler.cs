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
}
