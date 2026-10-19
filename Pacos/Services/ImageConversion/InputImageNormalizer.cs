using Pacos.Models;
using SkiaSharp;

namespace Pacos.Services.ImageConversion;

/// <summary>
/// Re-encodes incoming WebP images as PNG before they reach the model. The Gemini
/// backend rejects animated WebP (e.g. some Telegram stickers) with a bare
/// <c>INVALID_ARGUMENT (code 400)</c>, failing the whole turn. Decoding keeps only
/// the first frame and PNG preserves transparency. Best-effort: undecodable input
/// is returned unchanged.
/// </summary>
public sealed class InputImageNormalizer
{
    private const string WebpMimeType = "image/webp";
    private const string PngMimeType = "image/png";
    private const int PngQuality = 100;

    private readonly ILogger<InputImageNormalizer> _logger;

    public InputImageNormalizer(ILogger<InputImageNormalizer> logger)
    {
        _logger = logger;
    }

    public ChatInputFile Normalize(ChatInputFile file)
    {
        if (!string.Equals(file.MimeType, WebpMimeType, StringComparison.OrdinalIgnoreCase))
        {
            return file;
        }

        try
        {
            using var decoded = SKBitmap.Decode(file.Bytes);
            if (decoded is null)
            {
                _logger.LogWarning("Could not decode {Origin} {MimeType} attachment; passing it through unchanged", file.Origin, file.MimeType);
                return file;
            }

            using var image = SKImage.FromBitmap(decoded);
            using var data = image.Encode(SKEncodedImageFormat.Png, PngQuality);
            if (data is null)
            {
                _logger.LogWarning("Could not PNG-encode {Origin} {MimeType} attachment; passing it through unchanged", file.Origin, file.MimeType);
                return file;
            }

            var pngBytes = data.ToArray();
            _logger.LogInformation(
                "Re-encoded {Origin} {MimeType} attachment ({OriginalBytes} bytes) as {Width}x{Height} PNG ({PngBytes} bytes)",
                file.Origin,
                file.MimeType,
                file.Bytes.Length,
                decoded.Width,
                decoded.Height,
                pngBytes.Length);

            return file with { Bytes = pngBytes, MimeType = PngMimeType, };
        }
        catch (Exception e)
        {
            _logger.LogWarning(e, "Failed to re-encode {Origin} {MimeType} attachment; passing it through unchanged", file.Origin, file.MimeType);
            return file;
        }
    }
}
