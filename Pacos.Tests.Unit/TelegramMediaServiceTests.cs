using Pacos.Services;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

namespace Pacos.Tests.Unit;

[TestFixture]
[Parallelizable(scope: ParallelScope.All)]
internal sealed class TelegramMediaServiceTests
{
    [Test]
    public void GetFileMetadata_WhenStickerIsStatic_ShouldReturnWebp()
    {
        var message = new Message { Sticker = new Sticker { FileId = "static", Type = StickerType.Regular, }, };

        var metadata = TelegramMediaService.GetFileMetadata(message);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(metadata?.FileId, Is.EqualTo("static"));
            Assert.That(metadata?.MimeType, Is.EqualTo("image/webp"));
        }
    }

    [Test]
    public void GetFileMetadata_WhenStickerIsVideo_ShouldReturnWebm()
    {
        var message = new Message { Sticker = new Sticker { FileId = "video", Type = StickerType.Regular, IsVideo = true, }, };

        var metadata = TelegramMediaService.GetFileMetadata(message);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(metadata?.FileId, Is.EqualTo("video"));
            Assert.That(metadata?.MimeType, Is.EqualTo("video/webm"));
        }
    }

    [Test]
    public void GetFileMetadata_WhenStickerIsAnimatedWithThumbnail_ShouldReturnThumbnail()
    {
        var message = new Message
        {
            Sticker = new Sticker
            {
                FileId = "animated",
                Type = StickerType.Regular,
                IsAnimated = true,
                Thumbnail = new PhotoSize { FileId = "thumbnail", FileSize = 1234, },
            },
        };

        var metadata = TelegramMediaService.GetFileMetadata(message);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(metadata?.FileId, Is.EqualTo("thumbnail"));
            Assert.That(metadata?.MimeType, Is.EqualTo("image/webp"));
            Assert.That(metadata?.FileSize, Is.EqualTo(1234));
        }
    }

    [Test]
    public void GetFileMetadata_WhenStickerIsAnimatedWithoutThumbnail_ShouldReturnNull()
    {
        var message = new Message { Sticker = new Sticker { FileId = "animated", Type = StickerType.Regular, IsAnimated = true, }, };

        Assert.That(TelegramMediaService.GetFileMetadata(message), Is.Null);
    }
}
