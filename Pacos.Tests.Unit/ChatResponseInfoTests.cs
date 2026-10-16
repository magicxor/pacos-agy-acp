using Pacos.Constants;
using Pacos.Models;

namespace Pacos.Tests.Unit;

[TestFixture]
[Parallelizable(scope: ParallelScope.All)]
internal sealed class ChatResponseInfoTests
{
    [Test]
    public void ToReplyText_WhenComplete_ReturnsTheTextAsIs()
    {
        var response = new ChatResponseInfo("готовый ответ", []);

        Assert.That(response.ToReplyText(100), Is.EqualTo("готовый ответ"));
    }

    [Test]
    public void ToReplyText_WhenTimedOutWithText_AppendsTheNotice()
    {
        var response = new ChatResponseInfo("частичный ответ", [], TimedOut: true);

        Assert.That(response.ToReplyText(1000), Is.EqualTo($"частичный ответ\n\n{Const.TurnTimeoutNotice}"));
    }

    [Test]
    public void ToReplyText_WhenTimedOutWithOnlyFiles_ReturnsJustTheNotice()
    {
        var response = new ChatResponseInfo(string.Empty, [new OutputFile("chart.png", [1])], TimedOut: true);

        Assert.That(response.ToReplyText(1000), Is.EqualTo(Const.TurnTimeoutNotice));
    }

    [Test]
    public void ToReplyText_WhenTimedOutAndTooLong_KeepsTheNoticeWithinTheLimit()
    {
        const int maxLength = 200;
        var response = new ChatResponseInfo(new string('x', 5000), [], TimedOut: true);

        var replyText = response.ToReplyText(maxLength);

        Assert.Multiple(() =>
        {
            Assert.That(replyText, Has.Length.EqualTo(maxLength));
            Assert.That(replyText, Does.EndWith(Const.TurnTimeoutNotice));
        });
    }

    [Test]
    public void ToReplyText_WhenLimitIsShorterThanTheNotice_CutsTheNotice()
    {
        var response = new ChatResponseInfo("частичный ответ", [], TimedOut: true);

        Assert.That(response.ToReplyText(20), Has.Length.EqualTo(20));
    }
}
