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
    public void GetQuotedFragment_WhenQuoteFromRichMessage_ShouldReturnRichTextsAsPlainText()
    {
        var message = new Message
        {
            ReplyToMessage = new Message { RichMessage = new RichMessage(), },
            Quote = new TextQuote
            {
                Text = string.Empty,
                RichTexts =
                [
                    new RichTextArray
                    {
                        Array =
                        [
                            new RichTextText { Text = "конец ", },
                            new RichTextBold { Text = new RichTextText { Text = "первого", }, },
                        ],
                    },
                    new RichTextItalic { Text = new RichTextText { Text = "начало второго", }, },
                ],
            },
        };

        Assert.That(message.GetQuotedFragment(), Is.EqualTo("конец первого\nначало второго"));
    }

    [Test]
    public void GetQuotedFragment_WhenQuoteHasBothTextAndRichTexts_ShouldPreferText()
    {
        var message = new Message
        {
            Quote = new TextQuote
            {
                Text = "простой текст",
                RichTexts = [new RichTextText { Text = "rich-текст", },],
            },
        };

        Assert.That(message.GetQuotedFragment(), Is.EqualTo("простой текст"));
    }

    [Test]
    public void GetQuotedFragment_WhenRichTextsAreBlank_ShouldReturnNull()
    {
        var message = new Message
        {
            Quote = new TextQuote
            {
                Text = string.Empty,
                RichTexts = [new RichTextText { Text = "  ", },],
            },
        };

        Assert.That(message.GetQuotedFragment(), Is.Null);
    }

    [Test]
    public void GetQuotedFragment_WhenNoQuote_ShouldReturnNull()
    {
        var message = new Message { ReplyToMessage = new Message { Text = "полное сообщение", }, };
        Assert.That(message.GetQuotedFragment(), Is.Null);
    }
}
