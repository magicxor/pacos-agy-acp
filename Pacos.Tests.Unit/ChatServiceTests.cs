using Pacos.Models;
using Pacos.Services.Acp;
using Pacos.Services.GenerativeAi;

namespace Pacos.Tests.Unit;

[TestFixture]
[Parallelizable(scope: ParallelScope.All)]
internal sealed class ChatServiceTests
{
    private static readonly OutputFile Chart = new("chart.png", [1, 2, 3]);

    [Test]
    public void ToChatResponse_WhenTimedOutWithNothingUsable_Throws()
    {
        Assert.Throws<AgyTurnTimeoutException>(() =>
            ChatService.ToChatResponse(new AcpPromptResult("  ", TimedOut: true), []));
    }

    [Test]
    public void ToChatResponse_WhenTimedOutWithOnlyFiles_DeliversTheFiles()
    {
        var response = ChatService.ToChatResponse(new AcpPromptResult(string.Empty, TimedOut: true), [Chart]);

        Assert.Multiple(() =>
        {
            Assert.That(response.Text, Is.Empty);
            Assert.That(response.Files, Is.EqualTo(new[] { Chart }));
            Assert.That(response.TimedOut, Is.True);
        });
    }

    [Test]
    public void ToChatResponse_WhenTimedOutWithPartialText_DeliversTheTextAsPartial()
    {
        var response = ChatService.ToChatResponse(new AcpPromptResult(" частичный ответ ", TimedOut: true), []);

        Assert.Multiple(() =>
        {
            Assert.That(response.Text, Is.EqualTo("частичный ответ"));
            Assert.That(response.TimedOut, Is.True);
        });
    }

    [Test]
    public void ToChatResponse_WhenNotTimedOutAndEmpty_ReturnsEmptyReplyForTheRetryPolicy()
    {
        var response = ChatService.ToChatResponse(new AcpPromptResult(string.Empty, TimedOut: false), []);

        Assert.Multiple(() =>
        {
            Assert.That(response.Text, Is.Empty);
            Assert.That(response.Files, Is.Empty);
            Assert.That(response.TimedOut, Is.False);
        });
    }

    [Test]
    public void ResolveFileNames_WhenNoAttachments_ReturnsNoNames()
    {
        Assert.That(ChatService.ResolveFileNames([], 42), Is.Empty);
    }

    [Test]
    public void ResolveFileNames_WhenEachMessageHasOneFile_KeepsPlainNames()
    {
        var names = ChatService.ResolveFileNames(
            [InputFile("image/jpeg", ChatInputOrigin.UserMessage), InputFile("video/mp4", ChatInputOrigin.RepliedMessage)],
            42);

        Assert.That(string.Join(' ', names), Is.EqualTo("user_message_42.jpg replied_message_42.mp4"));
    }

    [Test]
    public void ResolveFileNames_WhenMessageHasSeveralFiles_NumbersThemInOrder()
    {
        var names = ChatService.ResolveFileNames(
            [
                InputFile("image/jpeg", ChatInputOrigin.RepliedMessage),
                InputFile("image/jpeg", ChatInputOrigin.RepliedMessage),
                InputFile("application/pdf", ChatInputOrigin.RepliedMessage),
            ],
            42);

        Assert.That(string.Join(' ', names), Is.EqualTo("replied_message_42_1.jpg replied_message_42_2.jpg replied_message_42_3.pdf"));
    }

    [Test]
    public void ResolveFileNames_WhenBothMessagesHaveFiles_NumbersEachMessageSeparately()
    {
        var names = ChatService.ResolveFileNames(
            [
                InputFile("image/png", ChatInputOrigin.UserMessage),
                InputFile("image/jpeg", ChatInputOrigin.RepliedMessage),
                InputFile("audio/ogg", ChatInputOrigin.RepliedMessage),
            ],
            42);

        Assert.That(string.Join(' ', names), Is.EqualTo("user_message_42.png replied_message_42_1.jpg replied_message_42_2.ogg"));
    }

    [Test]
    public void ResolveFileNames_WhenMimeTypeIsUnknown_UsesBinExtension()
    {
        var names = ChatService.ResolveFileNames([InputFile("application/zip", ChatInputOrigin.UserMessage)], 42);

        Assert.That(string.Join(' ', names), Is.EqualTo("user_message_42.bin"));
    }

    private static ChatInputFile InputFile(string mimeType, ChatInputOrigin origin) => new([1, 2, 3], mimeType, origin);
}
