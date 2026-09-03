using Pacos.Services.ChatCommandHandlers;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

namespace Pacos.Tests.Unit;

[TestFixture]
[Parallelizable(scope: ParallelScope.All)]
internal sealed class MentionHandlerTests
{
    [Test]
    public void ResolveRepliedToText_WhenQuotePresent_ShouldReturnQuotedFragmentOnly()
    {
        var updateMessage = new Message
        {
            Text = "@pacos что это значит?",
            ReplyToMessage = new Message { Text = "Первый абзац.\n\nВторой абзац.\n\nТретий абзац.", },
            Quote = new TextQuote { Text = "Второй абзац.", Position = 16, IsManual = true, },
        };

        var (text, isQuotedFragment) = MentionHandler.ResolveRepliedToText(updateMessage);

        Assert.Multiple(() =>
        {
            Assert.That(text, Is.EqualTo("Второй абзац."));
            Assert.That(isQuotedFragment, Is.True);
        });
    }

    [Test]
    public void ResolveRepliedToText_WhenQuoteHasSurroundingWhitespace_ShouldTrimIt()
    {
        var updateMessage = new Message
        {
            ReplyToMessage = new Message { Text = "полное сообщение", },
            Quote = new TextQuote { Text = "  часть сообщения \n", },
        };

        var (text, isQuotedFragment) = MentionHandler.ResolveRepliedToText(updateMessage);

        Assert.Multiple(() =>
        {
            Assert.That(text, Is.EqualTo("часть сообщения"));
            Assert.That(isQuotedFragment, Is.True);
        });
    }

    [Test]
    public void ResolveRepliedToText_WhenQuoteWithoutReplyToMessage_ShouldReturnQuotedFragment()
    {
        var updateMessage = new Message
        {
            Quote = new TextQuote { Text = "часть чужого сообщения", },
        };

        var (text, isQuotedFragment) = MentionHandler.ResolveRepliedToText(updateMessage);

        Assert.Multiple(() =>
        {
            Assert.That(text, Is.EqualTo("часть чужого сообщения"));
            Assert.That(isQuotedFragment, Is.True);
        });
    }

    [Test]
    public void ResolveRepliedToText_WhenQuoteEmpty_ShouldFallBackToFullText()
    {
        var updateMessage = new Message
        {
            ReplyToMessage = new Message { Text = "полное сообщение", },
            Quote = new TextQuote { Text = "   ", },
        };

        var (text, isQuotedFragment) = MentionHandler.ResolveRepliedToText(updateMessage);

        Assert.Multiple(() =>
        {
            Assert.That(text, Is.EqualTo("полное сообщение"));
            Assert.That(isQuotedFragment, Is.False);
        });
    }

    [Test]
    public void ResolveRepliedToText_WhenNoQuote_ShouldReturnFullRepliedText()
    {
        var updateMessage = new Message
        {
            ReplyToMessage = new Message { Text = " полное сообщение ", },
        };

        var (text, isQuotedFragment) = MentionHandler.ResolveRepliedToText(updateMessage);

        Assert.Multiple(() =>
        {
            Assert.That(text, Is.EqualTo("полное сообщение"));
            Assert.That(isQuotedFragment, Is.False);
        });
    }

    [Test]
    public void ResolveRepliedToText_WhenNoQuoteAndOnlyCaption_ShouldReturnCaption()
    {
        var updateMessage = new Message
        {
            ReplyToMessage = new Message { Caption = "подпись к фото", },
        };

        var (text, isQuotedFragment) = MentionHandler.ResolveRepliedToText(updateMessage);

        Assert.Multiple(() =>
        {
            Assert.That(text, Is.EqualTo("подпись к фото"));
            Assert.That(isQuotedFragment, Is.False);
        });
    }

    [Test]
    public void ResolveRepliedToText_WhenNoQuoteAndPoll_ShouldReturnPollText()
    {
        var updateMessage = new Message
        {
            ReplyToMessage = new Message
            {
                Poll = new Poll
                {
                    Question = "Кто?",
                    Options = [new PollOption { Text = "я", }, new PollOption { Text = "не я", },],
                },
            },
        };

        var (text, isQuotedFragment) = MentionHandler.ResolveRepliedToText(updateMessage);

        Assert.Multiple(() =>
        {
            Assert.That(text, Is.EqualTo("Poll: Кто? | Description:  | Options: 1) я, 2) не я"));
            Assert.That(isQuotedFragment, Is.False);
        });
    }

    [Test]
    public void ResolveRepliedToText_WhenNotAReply_ShouldReturnEmpty()
    {
        var (text, isQuotedFragment) = MentionHandler.ResolveRepliedToText(new Message { Text = "@pacos привет", });

        Assert.Multiple(() =>
        {
            Assert.That(text, Is.Empty);
            Assert.That(isQuotedFragment, Is.False);
        });
    }

    [Test]
    public void ResolveRepliedToAuthor_WhenRepliedToMessageHasUsername_ShouldUseIt()
    {
        var updateMessage = new Message
        {
            ReplyToMessage = new Message
            {
                From = new User { Id = 1, FirstName = "Bob", LastName = "Smith", Username = "bob", },
            },
        };

        var (repliedToAuthor, forwardSource) = MentionHandler.ResolveRepliedToAuthor(updateMessage);

        Assert.Multiple(() =>
        {
            Assert.That(repliedToAuthor, Is.EqualTo("bob"));
            Assert.That(forwardSource, Is.Null);
        });
    }

    [Test]
    public void ResolveRepliedToAuthor_WhenRepliedToMessageHasNoUsername_ShouldUseFullName()
    {
        var updateMessage = new Message
        {
            ReplyToMessage = new Message { From = new User { Id = 1, FirstName = "Bob", LastName = "Smith", }, },
        };

        var (repliedToAuthor, _) = MentionHandler.ResolveRepliedToAuthor(updateMessage);

        Assert.That(repliedToAuthor, Is.EqualTo("Bob Smith"));
    }

    [Test]
    public void ResolveRepliedToAuthor_WhenRepliedToMessageIsForwarded_ShouldReturnForwardSource()
    {
        var updateMessage = new Message
        {
            ReplyToMessage = new Message
            {
                From = new User { Id = 1, FirstName = "Bob", Username = "bob", },
                ForwardOrigin = new MessageOriginChannel
                {
                    Chat = new Chat { Id = -100, Type = ChatType.Channel, Title = "News", },
                    MessageId = 7,
                },
            },
        };

        var (repliedToAuthor, forwardSource) = MentionHandler.ResolveRepliedToAuthor(updateMessage);

        Assert.Multiple(() =>
        {
            Assert.That(repliedToAuthor, Is.EqualTo("bob"));
            Assert.That(forwardSource, Is.EqualTo("channel \"News\""));
        });
    }

    [Test]
    public void ResolveRepliedToAuthor_WhenRepliedToMessageHasNoSender_ShouldFallBackToUnknownAuthor()
    {
        var (repliedToAuthor, forwardSource) = MentionHandler.ResolveRepliedToAuthor(new Message { ReplyToMessage = new Message(), });

        Assert.Multiple(() =>
        {
            Assert.That(repliedToAuthor, Is.EqualTo("Original Poster"));
            Assert.That(forwardSource, Is.Null);
        });
    }

    [Test]
    public void ResolveRepliedToAuthor_WhenExternalReplyFromUser_ShouldUseOriginUser()
    {
        var updateMessage = new Message
        {
            Quote = new TextQuote { Text = "часть чужого сообщения", },
            ExternalReply = new ExternalReplyInfo
            {
                Origin = new MessageOriginUser { SenderUser = new User { Id = 1, FirstName = "Bob", Username = "bob", }, },
            },
        };

        var (repliedToAuthor, forwardSource) = MentionHandler.ResolveRepliedToAuthor(updateMessage);

        Assert.Multiple(() =>
        {
            Assert.That(repliedToAuthor, Is.EqualTo("user bob"));
            Assert.That(forwardSource, Is.Null);
        });
    }

    [Test]
    public void ResolveRepliedToAuthor_WhenExternalReplyFromChannel_ShouldUseOriginChannel()
    {
        var updateMessage = new Message
        {
            Quote = new TextQuote { Text = "часть поста", },
            ExternalReply = new ExternalReplyInfo
            {
                Origin = new MessageOriginChannel
                {
                    Chat = new Chat { Id = -100, Type = ChatType.Channel, Title = "News", },
                    MessageId = 7,
                    AuthorSignature = "Editor",
                },
            },
        };

        var (repliedToAuthor, forwardSource) = MentionHandler.ResolveRepliedToAuthor(updateMessage);

        Assert.Multiple(() =>
        {
            Assert.That(repliedToAuthor, Is.EqualTo("channel \"News\" (Editor)"));
            Assert.That(forwardSource, Is.Null);
        });
    }

    [Test]
    public void ResolveRepliedToAuthor_WhenNotAReply_ShouldFallBackToUnknownAuthor()
    {
        var (repliedToAuthor, forwardSource) = MentionHandler.ResolveRepliedToAuthor(new Message { Text = "@pacos привет", });

        Assert.Multiple(() =>
        {
            Assert.That(repliedToAuthor, Is.EqualTo("Original Poster"));
            Assert.That(forwardSource, Is.Null);
        });
    }

    [Test]
    public void BuildRepliedToHeader_WhenQuotedFragment_ShouldMarkItAsQuotedPart()
    {
        var header = MentionHandler.BuildRepliedToHeader("someone", forwardSource: null, isQuotedFragment: true);
        Assert.That(header, Is.EqualTo("--- Quoted Part of the Message by someone: ---"));
    }

    [Test]
    public void BuildRepliedToHeader_WhenQuotedFragmentOfForward_ShouldKeepForwardSource()
    {
        var header = MentionHandler.BuildRepliedToHeader("someone", "channel \"News\"", isQuotedFragment: true);
        Assert.That(header, Is.EqualTo("--- Quoted Part of the Message by someone (forwarded from channel \"News\"): ---"));
    }

    [Test]
    public void BuildRepliedToHeader_WhenFullMessage_ShouldDescribeOriginalMessage()
    {
        var header = MentionHandler.BuildRepliedToHeader("someone", forwardSource: null, isQuotedFragment: false);
        Assert.That(header, Is.EqualTo("--- Original Message by someone: ---"));
    }

    [Test]
    public void BuildRepliedToHeader_WhenFullForwardedMessage_ShouldKeepForwardSource()
    {
        var header = MentionHandler.BuildRepliedToHeader("someone", "user bob", isQuotedFragment: false);
        Assert.That(header, Is.EqualTo("--- Original Message by someone (forwarded from user bob): ---"));
    }
}
