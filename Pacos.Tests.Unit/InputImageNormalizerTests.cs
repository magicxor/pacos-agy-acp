using System.Buffers.Binary;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Pacos.Models;
using Pacos.Services.ImageConversion;
using SkiaSharp;

namespace Pacos.Tests.Unit;

[TestFixture]
[Parallelizable(scope: ParallelScope.All)]
internal sealed class InputImageNormalizerTests
{
    [Test]
    public void Normalize_WhenWebpIsAnimated_ShouldReturnFirstFrameAsPng()
    {
        var animatedWebp = CreateAnimatedWebp(64, 48, SKColors.Red, SKColors.Blue);
        var file = new ChatInputFile(animatedWebp, "image/webp", ChatInputOrigin.RepliedMessage);

        var result = CreateNormalizer().Normalize(file);
        using var data = SKData.CreateCopy(result.Bytes);
        using var codec = SKCodec.Create(data);
        using var bitmap = SKBitmap.Decode(result.Bytes);
        var pixel = bitmap.GetPixel(32, 24);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(GetFrameCount(animatedWebp), Is.EqualTo(2));
            Assert.That(result.MimeType, Is.EqualTo("image/png"));
            Assert.That(result.Origin, Is.EqualTo(ChatInputOrigin.RepliedMessage));
            Assert.That(codec.EncodedFormat, Is.EqualTo(SKEncodedImageFormat.Png));
            Assert.That(codec.FrameCount, Is.LessThanOrEqualTo(1));
            Assert.That((codec.Info.Width, codec.Info.Height), Is.EqualTo((64, 48)));
            Assert.That(pixel.Red, Is.GreaterThan(200));
            Assert.That(pixel.Blue, Is.LessThan(50));
        }
    }

    [Test]
    public void Normalize_WhenWebpIsStatic_ShouldReencodeAsPng()
    {
        var file = new ChatInputFile(EncodeSolidImage(32, 32, SKColors.Green), "image/webp", ChatInputOrigin.UserMessage);

        var result = CreateNormalizer().Normalize(file);
        using var data = SKData.CreateCopy(result.Bytes);
        using var codec = SKCodec.Create(data);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.MimeType, Is.EqualTo("image/png"));
            Assert.That(codec.EncodedFormat, Is.EqualTo(SKEncodedImageFormat.Png));
        }
    }

    [TestCase("image/jpeg")]
    [TestCase("video/webm")]
    public void Normalize_WhenMimeTypeIsNotWebp_ShouldReturnOriginalUnchanged(string mimeType)
    {
        var file = new ChatInputFile([1, 2, 3, 4], mimeType, ChatInputOrigin.UserMessage);

        var result = CreateNormalizer().Normalize(file);

        Assert.That(result, Is.SameAs(file));
    }

    [Test]
    public void Normalize_WhenWebpCannotBeDecoded_ShouldReturnOriginalUnchanged()
    {
        var file = new ChatInputFile([1, 2, 3, 4], "image/webp", ChatInputOrigin.UserMessage);

        var result = CreateNormalizer().Normalize(file);

        Assert.That(result, Is.SameAs(file));
    }

    private static InputImageNormalizer CreateNormalizer() => new(NullLogger<InputImageNormalizer>.Instance);

    private static int GetFrameCount(byte[] bytes)
    {
        using var data = SKData.CreateCopy(bytes);
        using var codec = SKCodec.Create(data);

        return codec.FrameCount;
    }

    private static byte[] EncodeSolidImage(int width, int height, SKColor color)
    {
        using var bitmap = new SKBitmap(width, height);
        bitmap.Erase(color);
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Webp, 100)
            ?? throw new InvalidOperationException("Failed to encode the test image.");

        return data.ToArray();
    }

    // Skia can't encode animated WebP, so wrap single-frame bitstreams into a
    // RIFF container with VP8X/ANIM/ANMF chunks per the WebP container spec.
    private static byte[] CreateAnimatedWebp(int width, int height, params SKColor[] frameColors)
    {
        using var stream = new MemoryStream();
        stream.Write("RIFF"u8);
        stream.Write(new byte[4]);
        stream.Write("WEBP"u8);

        const byte alphaAndAnimationFlags = 0x12;
        WriteChunk(stream, "VP8X", [alphaAndAnimationFlags, 0, 0, 0, .. ToUInt24(width - 1), .. ToUInt24(height - 1)]);
        WriteChunk(stream, "ANIM", [0, 0, 0, 0, 0, 0]);

        const byte doNotBlend = 0x02;
        foreach (var color in frameColors)
        {
            byte[] header =
            [
                .. ToUInt24(0),
                .. ToUInt24(0),
                .. ToUInt24(width - 1),
                .. ToUInt24(height - 1),
                .. ToUInt24(100),
                doNotBlend,
            ];
            WriteChunk(stream, "ANMF", [.. header, .. ExtractFrameChunks(EncodeSolidImage(width, height, color))]);
        }

        var bytes = stream.ToArray();
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4), (uint)(bytes.Length - 8));

        return bytes;
    }

    private static byte[] ExtractFrameChunks(byte[] webp)
    {
        using var frame = new MemoryStream();
        var position = 12;
        while (position + 8 <= webp.Length)
        {
            var fourCc = Encoding.ASCII.GetString(webp, position, 4);
            var size = (int)BinaryPrimitives.ReadUInt32LittleEndian(webp.AsSpan(position + 4));
            var chunkLength = 8 + size + (size % 2);
            if (fourCc != "VP8X")
            {
                frame.Write(webp, position, chunkLength);
            }

            position += chunkLength;
        }

        return frame.ToArray();
    }

    private static void WriteChunk(MemoryStream stream, string fourCc, byte[] payload)
    {
        stream.Write(Encoding.ASCII.GetBytes(fourCc));
        Span<byte> size = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(size, (uint)payload.Length);
        stream.Write(size);
        stream.Write(payload);
        if (payload.Length % 2 == 1)
        {
            stream.WriteByte(0);
        }
    }

    private static byte[] ToUInt24(int value) => [(byte)value, (byte)(value >> 8), (byte)(value >> 16)];
}
