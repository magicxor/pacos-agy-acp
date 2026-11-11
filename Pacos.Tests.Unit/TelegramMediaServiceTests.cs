using Microsoft.Extensions.Logging.Abstractions;
using Pacos.Models;
using Pacos.Services;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

namespace Pacos.Tests.Unit;

[TestFixture]
[Parallelizable(scope: ParallelScope.All)]
internal sealed class TelegramMediaServiceTests
{
    private static readonly TelegramFileMetadata PhotoMetadata = new("photo", "image/jpeg", 1234);

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

    [Test]
    public void GetAllFileMetadata_WhenMessageIsNull_ShouldReturnEmpty()
    {
        Assert.That(TelegramMediaService.GetAllFileMetadata(null), Is.Empty);
    }

    [Test]
    public void GetAllFileMetadata_WhenMessageHasNoMedia_ShouldReturnEmpty()
    {
        var message = RichMessageOf(new RichBlockParagraph { Text = new RichTextText { Text = "только текст", }, }, new RichBlockDivider());

        Assert.That(TelegramMediaService.GetAllFileMetadata(message), Is.Empty);
    }

    [Test]
    public void GetAllFileMetadata_WhenRegularPhotoMessage_ShouldReturnItsSingleFile()
    {
        var message = new Message { Photo = [new PhotoSize { FileId = "photo", Width = 100, FileSize = 1234, }], Caption = "подпись", };

        Assert.That(TelegramMediaService.GetAllFileMetadata(message), Is.EqualTo(new[] { new TelegramFileMetadata("photo", "image/jpeg", 1234), }));
    }

    private static IEnumerable<TestCaseData> RichMediaBlockCases()
    {
        yield return new TestCaseData(RichPhoto("photo"), new TelegramFileMetadata("photo", "image/jpeg", null));
        yield return new TestCaseData(
            new RichBlockVideo { Video = new Video { FileId = "video", MimeType = "video/quicktime", FileSize = 10, }, },
            new TelegramFileMetadata("video", "video/quicktime", 10));
        yield return new TestCaseData(
            new RichBlockVideo { Video = new Video { FileId = "video-without-mime", }, },
            new TelegramFileMetadata("video-without-mime", "video/mp4", null));
        yield return new TestCaseData(
            new RichBlockAnimation { Animation = new Animation { FileId = "animation", FileSize = 20, }, },
            new TelegramFileMetadata("animation", "video/mp4", 20));
        yield return new TestCaseData(
            new RichBlockAudio { Audio = new Audio { FileId = "audio", FileSize = 30, }, },
            new TelegramFileMetadata("audio", "audio/mpeg", 30));
        yield return new TestCaseData(
            new RichBlockVoiceNote { VoiceNote = new Voice { FileId = "voice", FileSize = 40, }, },
            new TelegramFileMetadata("voice", "audio/ogg", 40));
        yield return new TestCaseData(
            new RichBlockDocument { Document = new Document { FileId = "document", MimeType = "application/pdf", FileSize = 50, }, },
            new TelegramFileMetadata("document", "application/pdf", 50));
    }

    [TestCaseSource(nameof(RichMediaBlockCases))]
    public void GetAllFileMetadata_WhenRichMediaBlock_ShouldReturnItsFile(RichBlock block, TelegramFileMetadata expected)
    {
        Assert.That(TelegramMediaService.GetAllFileMetadata(RichMessageOf(block)), Is.EqualTo(new[] { expected, }));
    }

    [Test]
    public void GetAllFileMetadata_WhenRichPhotoHasSeveralSizes_ShouldReturnTheBiggest()
    {
        var message = RichMessageOf(new RichBlockPhoto
        {
            Photo =
            [
                new PhotoSize { FileId = "small", Width = 90, },
                new PhotoSize { FileId = "big", Width = 1280, },
                new PhotoSize { FileId = "medium", Width = 320, },
            ],
        });

        Assert.That(AllFileIds(message), Is.EqualTo("big"));
    }

    [Test]
    public void GetAllFileMetadata_WhenRichBlockCarriesNoUsableFile_ShouldSkipIt()
    {
        var message = RichMessageOf(
            new RichBlockPhoto(),
            new RichBlockPhoto { Photo = [], },
            new RichBlockDocument { Document = new Document { FileId = "document-without-mime", }, },
            new RichBlockMap { Zoom = 14, });

        Assert.That(TelegramMediaService.GetAllFileMetadata(message), Is.Empty);
    }

    [Test]
    public void GetAllFileMetadata_WhenMediaIsNestedInContainers_ShouldReturnFilesInDocumentOrder()
    {
        var message = RichMessageOf(
            RichPhoto("top"),
            new RichBlockCollage { Blocks = [RichPhoto("collage-1"), RichPhoto("collage-2")], },
            new RichBlockSlideshow { Blocks = [RichPhoto("slideshow")], },
            new RichBlockBlockQuotation { Blocks = [RichPhoto("quotation")], },
            new RichBlockDetails
            {
                Summary = new RichTextText { Text = "подробности", },
                Blocks = [new RichBlockCollage { Blocks = [RichPhoto("details-collage")], }],
            },
            new RichBlockList
            {
                Items =
                [
                    new RichBlockListItem { Blocks = [RichPhoto("list-1")], },
                    new RichBlockListItem { Blocks = [RichPhoto("list-2")], },
                ],
            },
            RichPhoto("bottom"));

        Assert.That(
            AllFileIds(message),
            Is.EqualTo("top collage-1 collage-2 slideshow quotation details-collage list-1 list-2 bottom"));
    }

    [Test]
    public void GetAllFileMetadata_WhenMessageHasOwnMediaAndRichMedia_ShouldReturnOwnMediaFirst()
    {
        var message = new Message
        {
            Document = new Document { FileId = "own", MimeType = "application/pdf", },
            RichMessage = new RichMessage { Blocks = [RichPhoto("embedded")], },
        };

        Assert.That(AllFileIds(message), Is.EqualTo("own embedded"));
    }

    [Test]
    public void GetAllFileMetadata_WhenSameFileIsEmbeddedTwice_ShouldReturnItOnce()
    {
        var message = RichMessageOf(RichPhoto("first"), RichPhoto("second"), RichPhoto("first"));

        Assert.That(AllFileIds(message), Is.EqualTo("first second"));
    }

    [Test]
    public async Task DownloadMediaAsync_WhenGetFileFails_ShouldReturnErrorMessage()
    {
        const string errorMessage = "Bot API Service Failure: TimeoutRejectedException";
        var botClient = new ThrowingTelegramBotClient(() => new RequestException(errorMessage));

        var (fileBytes, actualErrorMessage) = await CreateService().DownloadMediaAsync(PhotoMetadata, botClient, CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(fileBytes, Is.Null);
            Assert.That(actualErrorMessage, Is.EqualTo(errorMessage));
        }
    }

    [Test]
    public async Task DownloadMediaAsync_WhenCancelled_ShouldRethrow()
    {
        using var cancellationTokenSource = new CancellationTokenSource();
        await cancellationTokenSource.CancelAsync();
        var botClient = new ThrowingTelegramBotClient(() => new OperationCanceledException(cancellationTokenSource.Token));

        Assert.CatchAsync<OperationCanceledException>(() => CreateService().DownloadMediaAsync(PhotoMetadata, botClient, cancellationTokenSource.Token));
    }

    private static TelegramMediaService CreateService() => new(NullLogger<TelegramMediaService>.Instance);

    private static RichBlockPhoto RichPhoto(string fileId) => new() { Photo = [new PhotoSize { FileId = fileId, Width = 100, }], };

    private static Message RichMessageOf(params RichBlock[] blocks) => new() { RichMessage = new RichMessage { Blocks = blocks, }, };

    private static string AllFileIds(Message message) =>
        string.Join(' ', TelegramMediaService.GetAllFileMetadata(message).Select(file => file.FileId));
}
