using Pacos.Extensions;
using Telegram.Bot.Types;

namespace Pacos.Tests.Unit;

[TestFixture]
[Parallelizable(scope: ParallelScope.All)]
internal sealed class MessageQuotedFragmentTests
{
    [Test]
    public void GetQuotedFragment_WhenQuotePresent_ShouldReturnQuotedText()
    {
        var message = new Message { Quote = new TextQuote { Text = "часть сообщения", }, };
        Assert.That(message.GetQuotedFragment(), Is.EqualTo("часть сообщения"));
    }

    [Test]
    public void GetQuotedFragment_WhenQuoteHasSurroundingWhitespace_ShouldTrimIt()
    {
        var message = new Message { Quote = new TextQuote { Text = " \nчасть сообщения\n ", }, };
        Assert.That(message.GetQuotedFragment(), Is.EqualTo("часть сообщения"));
    }

    [TestCase("")]
    [TestCase("   ")]
    [TestCase("\n\t")]
    public void GetQuotedFragment_WhenQuoteIsBlank_ShouldReturnNull(string quoteText)
    {
        var message = new Message { Quote = new TextQuote { Text = quoteText, }, };
        Assert.That(message.GetQuotedFragment(), Is.Null);
    }

    [Test]
    public void GetQuotedFragment_WhenNoQuote_ShouldReturnNull()
    {
        var message = new Message { ReplyToMessage = new Message { Text = "полное сообщение", }, };
        Assert.That(message.GetQuotedFragment(), Is.Null);
    }
}
