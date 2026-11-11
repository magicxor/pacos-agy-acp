using Pacos.Constants;
using Pacos.Services.ChatCommandHandlers;
using Telegram.Bot.Types;

namespace Pacos.Tests.Unit;

[TestFixture]
[Parallelizable(scope: ParallelScope.All)]
internal sealed class DrawHandlerTests
{
    private const string Suffix = " Обязательно сохрани результат как файл изображения в выходную директорию.";

    [Test]
    public void BuildDrawMessage_WhenPromptProvided_ShouldUsePrompt()
    {
        var result = DrawHandler.BuildDrawMessage("user", "кот в сапогах", string.Empty, 0);
        Assert.That(result, Is.EqualTo("user: Сгенерируй изображение по запросу: кот в сапогах." + Suffix));
    }

    [Test]
    public void BuildDrawMessage_WhenPromptEmptyAndRepliedTextPresent_ShouldVisualizeRepliedMessage()
    {
        var result = DrawHandler.BuildDrawMessage("user", string.Empty, "сегодня дождь", 0);
        Assert.That(result, Is.EqualTo("user: Сгенерируй изображение, визуализирующее следующее сообщение: сегодня дождь." + Suffix));
    }

    [Test]
    public void BuildDrawMessage_WhenPromptAndRepliedTextPresent_ShouldPreferPrompt()
    {
        var result = DrawHandler.BuildDrawMessage("user", "пчёлка", "сегодня дождь", 1);
        Assert.That(result, Is.EqualTo("user: Используя прикреплённое изображение как основу, сгенерируй новое изображение по запросу: пчёлка." + Suffix));
    }

    [Test]
    public void BuildDrawMessage_WhenPromptEmptyAndRepliedTextPresentWithImages_ShouldVisualizeRepliedMessage()
    {
        var result = DrawHandler.BuildDrawMessage("user", string.Empty, "сегодня дождь", 2);
        Assert.That(result, Is.EqualTo("user: Используя прикреплённые изображения как основу, сгенерируй новое изображение, визуализирующее следующее сообщение: сегодня дождь." + Suffix));
    }

    [Test]
    public void BuildDrawMessage_WhenPromptAndRepliedTextEmpty_ShouldUseBasisOnly()
    {
        var result = DrawHandler.BuildDrawMessage("user", string.Empty, string.Empty, 1);
        Assert.That(result, Is.EqualTo("user: Используя прикреплённое изображение как основу, сгенерируй новое изображение." + Suffix));
    }

    [Test]
    public void GetImageMetadata_WhenMessageIsNull_ShouldReturnEmpty()
    {
        Assert.That(DrawHandler.GetImageMetadata(null), Is.Empty);
    }

    [Test]
    public void GetImageMetadata_WhenRichMessageMixesMedia_ShouldReturnOnlyImagesInOrder()
    {
        var message = new Message
        {
            RichMessage = new RichMessage
            {
                Blocks =
                [
                    RichPhoto("first-photo"),
                    new RichBlockVideo { Video = new Video { FileId = "video", }, },
                    new RichBlockDocument { Document = new Document { FileId = "image-document", MimeType = "image/png", }, },
                    new RichBlockDocument { Document = new Document { FileId = "pdf-document", MimeType = "application/pdf", }, },
                    RichPhoto("last-photo"),
                ],
            },
        };

        Assert.That(
            string.Join(' ', DrawHandler.GetImageMetadata(message).Select(metadata => metadata.FileId)),
            Is.EqualTo("first-photo image-document last-photo"));
    }

    [Test]
    public void GetImageMetadata_WhenMessageHasMoreImagesThanTheLimit_ShouldKeepTheFirstOnes()
    {
        var photoIds = Enumerable.Range(1, Const.MaxInputFilesPerMessage + 2).Select(i => $"photo-{i}").ToArray();
        var message = new Message
        {
            RichMessage = new RichMessage { Blocks = [.. photoIds.Select(RichPhoto)], },
        };

        Assert.That(
            DrawHandler.GetImageMetadata(message).Select(metadata => metadata.FileId),
            Is.EqualTo(photoIds.Take(Const.MaxInputFilesPerMessage)));
    }

    private static RichBlockPhoto RichPhoto(string fileId) => new() { Photo = [new PhotoSize { FileId = fileId, Width = 100, }], };
}
