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
}
