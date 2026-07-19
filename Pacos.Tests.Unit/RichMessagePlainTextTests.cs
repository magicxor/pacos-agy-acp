using Pacos.Extensions;
using Telegram.Bot.Types;

namespace Pacos.Tests.Unit;

[TestFixture]
[Parallelizable(scope: ParallelScope.All)]
internal sealed class RichMessagePlainTextTests
{
    private static RichTextText Plain(string text) => new() { Text = text };

    private static RichTextBold Bold(RichText text) => new() { Text = text };

    private static RichTextSpoiler Spoiler(RichText text) => new() { Text = text };

    private static RichTextArray TextArray(params RichText[] parts) => new() { Array = parts };

    private static RichBlockParagraph Paragraph(string text) => new() { Text = Plain(text) };

    private static RichBlockParagraph Paragraph(RichText text) => new() { Text = text };

    private static RichBlockTableCell Cell(string text) => new() { Text = Plain(text) };

    private static RichMessage MessageOf(params RichBlock[] blocks) => new() { Blocks = blocks };

    // ---------- null / empty ----------

    [Test]
    public void GetPlainText_WhenMessageIsNull_ShouldReturnEmptyString()
    {
        const RichMessage? message = null;
        Assert.That(message.GetPlainText(), Is.EqualTo(string.Empty));
    }

    [Test]
    public void GetPlainText_WhenBlocksAreNotSet_ShouldReturnEmptyString()
    {
        var message = new RichMessage();
        Assert.That(message.GetPlainText(), Is.EqualTo(string.Empty));
    }

    [Test]
    public void GetPlainText_WhenBlocksAreEmpty_ShouldReturnEmptyString()
    {
        var message = MessageOf();
        Assert.That(message.GetPlainText(), Is.EqualTo(string.Empty));
    }

    // ---------- simple blocks ----------

    [Test]
    public void GetPlainText_WhenSingleParagraph_ShouldReturnItsTextWithoutTrailingNewline()
    {
        var message = MessageOf(Paragraph("Привет, мир!"));
        Assert.That(message.GetPlainText(), Is.EqualTo("Привет, мир!"));
    }

    [Test]
    public void GetPlainText_WhenMultipleParagraphs_ShouldSeparateThemWithNewlines()
    {
        var message = MessageOf(Paragraph("One"), Paragraph("Two"), Paragraph("Three"));
        var expected = string.Join('\n', "One", "Two", "Three");
        Assert.That(message.GetPlainText(), Is.EqualTo(expected));
    }

    [Test]
    public void GetPlainText_WhenParagraphTextIsNotSet_ShouldSkipIt()
    {
        var message = MessageOf(Paragraph("A"), new RichBlockParagraph(), Paragraph("B"));
        var expected = string.Join('\n', "A", "B");
        Assert.That(message.GetPlainText(), Is.EqualTo(expected));
    }

    [Test]
    public void GetPlainText_WhenParagraphTextIsEmpty_ShouldProduceEmptyLine()
    {
        var message = MessageOf(Paragraph("A"), Paragraph(string.Empty), Paragraph("B"));
        var expected = string.Join('\n', "A", string.Empty, "B");
        Assert.That(message.GetPlainText(), Is.EqualTo(expected));
    }

    [TestCase(1)]
    [TestCase(2)]
    [TestCase(3)]
    [TestCase(4)]
    [TestCase(5)]
    [TestCase(6)]
    public void GetPlainText_WhenSectionHeadingOfAnySize_ShouldReturnItsText(int size)
    {
        var message = MessageOf(new RichBlockSectionHeading { Text = Plain("Заголовок"), Size = size });
        Assert.That(message.GetPlainText(), Is.EqualTo("Заголовок"));
    }

    [Test]
    public void GetPlainText_WhenHeadingsAndParagraphsMixed_ShouldKeepDocumentOrder()
    {
        var message = MessageOf(
            new RichBlockSectionHeading { Text = Plain("Раздел 1"), Size = 1 },
            Paragraph("Текст раздела 1"),
            new RichBlockSectionHeading { Text = Plain("Раздел 2"), Size = 2 },
            Paragraph("Текст раздела 2"));
        var expected = string.Join('\n', "Раздел 1", "Текст раздела 1", "Раздел 2", "Текст раздела 2");
        Assert.That(message.GetPlainText(), Is.EqualTo(expected));
    }

    [Test]
    public void GetPlainText_WhenFooter_ShouldReturnItsText()
    {
        var message = MessageOf(new RichBlockFooter { Text = Plain("Footer text") });
        Assert.That(message.GetPlainText(), Is.EqualTo("Footer text"));
    }

    [Test]
    public void GetPlainText_WhenPreformatted_ShouldPreserveInnerNewlines()
    {
        var code = string.Join('\n', "print('a')", "print('b')");
        var message = MessageOf(new RichBlockPreformatted { Text = Plain(code), Language = "python" });
        Assert.That(message.GetPlainText(), Is.EqualTo(code));
    }

    [Test]
    public void GetPlainText_WhenMathematicalExpressionBlock_ShouldReturnLatexSource()
    {
        var message = MessageOf(new RichBlockMathematicalExpression { Expression = "E = mc^2" });
        Assert.That(message.GetPlainText(), Is.EqualTo("E = mc^2"));
    }

    [Test]
    public void GetPlainText_WhenMathematicalExpressionBlockIsEmpty_ShouldSkipIt()
    {
        var message = MessageOf(
            Paragraph("A"),
            new RichBlockMathematicalExpression { Expression = string.Empty },
            Paragraph("B"));
        var expected = string.Join('\n', "A", "B");
        Assert.That(message.GetPlainText(), Is.EqualTo(expected));
    }

    [Test]
    public void GetPlainText_WhenDividerBetweenParagraphs_ShouldContributeNothing()
    {
        var message = MessageOf(Paragraph("A"), new RichBlockDivider(), Paragraph("B"));
        var expected = string.Join('\n', "A", "B");
        Assert.That(message.GetPlainText(), Is.EqualTo(expected));
    }

    [Test]
    public void GetPlainText_WhenAnchorBlock_ShouldContributeNothing()
    {
        var message = MessageOf(new RichBlockAnchor { Name = "chapter-1" });
        Assert.That(message.GetPlainText(), Is.EqualTo(string.Empty));
    }

    [Test]
    public void GetPlainText_WhenMapWithoutCaption_ShouldContributeNothing()
    {
        var message = MessageOf(new RichBlockMap { Zoom = 14 });
        Assert.That(message.GetPlainText(), Is.EqualTo(string.Empty));
    }

    [Test]
    public void GetPlainText_WhenMapWithCaptionAndCredit_ShouldReturnBoth()
    {
        var message = MessageOf(new RichBlockMap
        {
            Zoom = 14,
            Caption = new RichBlockCaption { Text = Plain("Подпись карты"), Credit = Plain("Картограф") },
        });
        var expected = string.Join('\n', "Подпись карты", "Картограф");
        Assert.That(message.GetPlainText(), Is.EqualTo(expected));
    }

    [Test]
    public void GetPlainText_WhenThinkingBlock_ShouldReturnItsText()
    {
        var message = MessageOf(new RichBlockThinking { Text = Plain("Thinking...") });
        Assert.That(message.GetPlainText(), Is.EqualTo("Thinking..."));
    }

    // ---------- inline entities ----------

    private static IEnumerable<TestCaseData> InlineEntityCases()
    {
        yield return new TestCaseData(Plain("просто текст"), "просто текст");
        yield return new TestCaseData(new RichTextBold { Text = Plain("жирный") }, "жирный");
        yield return new TestCaseData(new RichTextItalic { Text = Plain("курсив") }, "курсив");
        yield return new TestCaseData(new RichTextUnderline { Text = Plain("подчёркнутый") }, "подчёркнутый");
        yield return new TestCaseData(new RichTextStrikethrough { Text = Plain("зачёркнутый") }, "зачёркнутый");
        yield return new TestCaseData(new RichTextSpoiler { Text = Plain("спойлер") }, "спойлер");
        yield return new TestCaseData(new RichTextCode { Text = Plain("код") }, "код");
        yield return new TestCaseData(new RichTextMarked { Text = Plain("выделенный") }, "выделенный");
        yield return new TestCaseData(new RichTextSubscript { Text = Plain("нижний индекс") }, "нижний индекс");
        yield return new TestCaseData(new RichTextSuperscript { Text = Plain("верхний индекс") }, "верхний индекс");
        yield return new TestCaseData(new RichTextUrl { Text = Plain("ссылка"), Url = "https://t.me/" }, "[ссылка](https://t.me/)");
        yield return new TestCaseData(new RichTextUrl { Text = Plain("https://t.me/"), Url = "https://t.me/" }, "https://t.me/");
        yield return new TestCaseData(new RichTextUrl { Text = Plain("без адреса"), Url = string.Empty }, "без адреса");
        yield return new TestCaseData(new RichTextEmailAddress { Text = Plain("почта"), EmailAddress = "user@example.com" }, "почта");
        yield return new TestCaseData(new RichTextPhoneNumber { Text = Plain("телефон"), PhoneNumber = "+123456789" }, "телефон");
        yield return new TestCaseData(new RichTextBankCardNumber { Text = Plain("4242 4242 4242 4242"), BankCardNumber = "4242424242424242" }, "4242 4242 4242 4242");
        yield return new TestCaseData(new RichTextMention { Text = Plain("@username"), Username = "username" }, "@username");
        yield return new TestCaseData(new RichTextTextMention { Text = Plain("Иван"), User = new User { FirstName = "Иван" } }, "Иван");
        yield return new TestCaseData(new RichTextHashtag { Text = Plain("#hashtag"), Hashtag = "hashtag" }, "#hashtag");
        yield return new TestCaseData(new RichTextCashtag { Text = Plain("$USD"), Cashtag = "USD" }, "$USD");
        yield return new TestCaseData(new RichTextBotCommand { Text = Plain("/command"), BotCommand = "command" }, "/command");
        yield return new TestCaseData(new RichTextDateTime { Text = Plain("22:45 завтра"), DateTimeFormat = "wDT" }, "22:45 завтра");
        yield return new TestCaseData(new RichTextAnchorLink { Text = Plain("к главе 1"), AnchorName = "chapter-1" }, "к главе 1");
        yield return new TestCaseData(new RichTextReference { Text = Plain("текст сноски"), Name = "note-1" }, "текст сноски");
        yield return new TestCaseData(new RichTextReferenceLink { Text = Plain("[1]"), ReferenceName = "note-1" }, "[1]");
        yield return new TestCaseData(new RichTextCustomEmoji { CustomEmojiId = "5368324170671202286", AlternativeText = "👍" }, "👍");
        yield return new TestCaseData(new RichTextCustomEmoji { CustomEmojiId = "5368324170671202286" }, string.Empty);
        yield return new TestCaseData(new RichTextMathematicalExpression { Expression = "x^2 + y^2" }, "x^2 + y^2");
        yield return new TestCaseData(new RichTextAnchor { Name = "chapter-1" }, string.Empty);
    }

    [TestCaseSource(nameof(InlineEntityCases))]
    public void GetPlainText_WhenParagraphContainsInlineEntity_ShouldExtractVisibleText(RichText inline, string expected)
    {
        var message = MessageOf(Paragraph(inline));
        Assert.That(message.GetPlainText(), Is.EqualTo(expected));
    }

    // ---------- rich text arrays and nesting ----------

    [Test]
    public void GetPlainText_WhenTextIsArrayOfSegments_ShouldConcatenateThem()
    {
        var message = MessageOf(Paragraph(TextArray(Plain("Привет, "), Bold(Plain("мир")), Plain("!"))));
        Assert.That(message.GetPlainText(), Is.EqualTo("Привет, мир!"));
    }

    [Test]
    public void GetPlainText_WhenArraysAreNested_ShouldConcatenateDepthFirst()
    {
        var message = MessageOf(Paragraph(TextArray(TextArray(Plain("a"), Plain("b")), Plain("c"))));
        Assert.That(message.GetPlainText(), Is.EqualTo("abc"));
    }

    [Test]
    public void GetPlainText_WhenArrayIsEmpty_ShouldReturnEmptyString()
    {
        var message = MessageOf(Paragraph(TextArray()));
        Assert.That(message.GetPlainText(), Is.EqualTo(string.Empty));
    }

    [Test]
    public void GetPlainText_WhenInlineAnchorInsideArray_ShouldSkipOnlyTheAnchor()
    {
        var message = MessageOf(Paragraph(TextArray(Plain("до"), new RichTextAnchor { Name = "a" }, Plain("после"))));
        Assert.That(message.GetPlainText(), Is.EqualTo("допосле"));
    }

    [Test]
    public void GetPlainText_WhenAllFormattingWrappersAreChained_ShouldReturnInnermostText()
    {
        RichText text = Plain("глубоко");
        text = new RichTextCode { Text = text };
        text = new RichTextMarked { Text = text };
        text = new RichTextSpoiler { Text = text };
        text = new RichTextStrikethrough { Text = text };
        text = new RichTextUnderline { Text = text };
        text = new RichTextItalic { Text = text };
        text = new RichTextBold { Text = text };

        var message = MessageOf(Paragraph(text));
        Assert.That(message.GetPlainText(), Is.EqualTo("глубоко"));
    }

    [Test]
    public void GetPlainText_WhenFormattingIsNestedToApiLimitDepth_ShouldReturnInnermostText()
    {
        RichText text = Plain("ядро");
        for (var i = 0; i < 16; i++)
        {
            text = i % 2 == 0
                ? Bold(text)
                : Spoiler(text);
        }

        var message = MessageOf(Paragraph(text));
        Assert.That(message.GetPlainText(), Is.EqualTo("ядро"));
    }

    [Test]
    public void GetPlainText_WhenSpoilerContainsMixedArray_ShouldReturnHiddenText()
    {
        var message = MessageOf(Paragraph(Spoiler(TextArray(Plain("скрытый "), Bold(Plain("текст"))))));
        Assert.That(message.GetPlainText(), Is.EqualTo("скрытый текст"));
    }

    [Test]
    public void GetPlainText_WhenSpoilerInsideSpoiler_ShouldReturnInnermostText()
    {
        var message = MessageOf(Paragraph(Spoiler(Spoiler(Plain("двойной спойлер")))));
        Assert.That(message.GetPlainText(), Is.EqualTo("двойной спойлер"));
    }

    [Test]
    public void GetPlainText_WhenHeadingContainsSpoiler_ShouldReturnFullHeadingText()
    {
        var heading = new RichBlockSectionHeading
        {
            Text = TextArray(Plain("Заголовок со "), Spoiler(Plain("спойлером"))),
            Size = 3,
        };
        var message = MessageOf(heading);
        Assert.That(message.GetPlainText(), Is.EqualTo("Заголовок со спойлером"));
    }

    // ---------- quotations ----------

    [Test]
    public void GetPlainText_WhenBlockQuotationWithMultipleParagraphs_ShouldReturnAllLines()
    {
        var message = MessageOf(new RichBlockBlockQuotation
        {
            Blocks = [Paragraph("Первая строка цитаты"), Paragraph("Последняя строка цитаты")],
        });
        var expected = string.Join('\n', "Первая строка цитаты", "Последняя строка цитаты");
        Assert.That(message.GetPlainText(), Is.EqualTo(expected));
    }

    [Test]
    public void GetPlainText_WhenBlockQuotationWithCredit_ShouldAppendCreditAfterContent()
    {
        var message = MessageOf(new RichBlockBlockQuotation
        {
            Blocks = [Paragraph("Цитата")],
            Credit = Plain("Автор"),
        });
        var expected = string.Join('\n', "Цитата", "Автор");
        Assert.That(message.GetPlainText(), Is.EqualTo(expected));
    }

    [Test]
    public void GetPlainText_WhenBlockQuotationsAreNested_ShouldAppendCreditsInnerFirst()
    {
        var inner = new RichBlockBlockQuotation
        {
            Blocks = [Paragraph("Внутренняя")],
            Credit = Plain("Внутренний автор"),
        };
        var outer = new RichBlockBlockQuotation
        {
            Blocks = [Paragraph("Внешняя"), inner],
            Credit = Plain("Внешний автор"),
        };
        var message = MessageOf(outer);
        var expected = string.Join('\n', "Внешняя", "Внутренняя", "Внутренний автор", "Внешний автор");
        Assert.That(message.GetPlainText(), Is.EqualTo(expected));
    }

    [Test]
    public void GetPlainText_WhenBlockQuotationContainsHeadingAndList_ShouldReturnAllLines()
    {
        var message = MessageOf(new RichBlockBlockQuotation
        {
            Blocks =
            [
                new RichBlockSectionHeading { Text = Plain("Заголовок в цитате"), Size = 4 },
                new RichBlockList
                {
                    Items = [new RichBlockListItem { Blocks = [Paragraph("Пункт в цитате")] }],
                },
            ],
        });
        var expected = string.Join('\n', "Заголовок в цитате", "Пункт в цитате");
        Assert.That(message.GetPlainText(), Is.EqualTo(expected));
    }

    [Test]
    public void GetPlainText_WhenPullQuotationWithCredit_ShouldReturnTextAndCredit()
    {
        var message = MessageOf(new RichBlockPullQuotation { Text = Plain("Цитата"), Credit = Plain("Автор") });
        var expected = string.Join('\n', "Цитата", "Автор");
        Assert.That(message.GetPlainText(), Is.EqualTo(expected));
    }

    [Test]
    public void GetPlainText_WhenPullQuotationWithoutCredit_ShouldReturnOnlyText()
    {
        var message = MessageOf(new RichBlockPullQuotation { Text = Plain("Цитата") });
        Assert.That(message.GetPlainText(), Is.EqualTo("Цитата"));
    }

    // ---------- details ----------

    [TestCase(true)]
    [TestCase(false)]
    public void GetPlainText_WhenDetailsBlock_ShouldReturnSummaryAndContentRegardlessOfOpenState(bool isOpen)
    {
        var message = MessageOf(new RichBlockDetails
        {
            Summary = Plain("Заголовок деталей"),
            Blocks = [Paragraph("Скрытое содержимое")],
            IsOpen = isOpen,
        });
        var expected = string.Join('\n', "Заголовок деталей", "Скрытое содержимое");
        Assert.That(message.GetPlainText(), Is.EqualTo(expected));
    }

    [Test]
    public void GetPlainText_WhenDetailsSummaryContainsFormatting_ShouldReturnItsPlainText()
    {
        var message = MessageOf(new RichBlockDetails
        {
            Summary = TextArray(Plain("Итоги с "), Bold(Plain("жирным"))),
            Blocks = [Paragraph("Содержимое")],
        });
        var expected = string.Join('\n', "Итоги с жирным", "Содержимое");
        Assert.That(message.GetPlainText(), Is.EqualTo(expected));
    }

    [Test]
    public void GetPlainText_WhenDetailsAreNested_ShouldReturnAllLevels()
    {
        var inner = new RichBlockDetails
        {
            Summary = Plain("Внутренние детали"),
            Blocks = [Paragraph("Внутреннее содержимое")],
        };
        var outer = new RichBlockDetails
        {
            Summary = Plain("Внешние детали"),
            Blocks = [Paragraph("Внешнее содержимое"), inner],
        };
        var message = MessageOf(outer);
        var expected = string.Join('\n', "Внешние детали", "Внешнее содержимое", "Внутренние детали", "Внутреннее содержимое");
        Assert.That(message.GetPlainText(), Is.EqualTo(expected));
    }

    // ---------- lists ----------

    [Test]
    public void GetPlainText_WhenSimpleList_ShouldReturnItemsOnSeparateLines()
    {
        var message = MessageOf(new RichBlockList
        {
            Items =
            [
                new RichBlockListItem { Blocks = [Paragraph("Первый")] },
                new RichBlockListItem { Blocks = [Paragraph("Второй")] },
            ],
        });
        var expected = string.Join('\n', "Первый", "Второй");
        Assert.That(message.GetPlainText(), Is.EqualTo(expected));
    }

    [Test]
    public void GetPlainText_WhenOrderedListWithLabels_ShouldPrefixItemsWithLabels()
    {
        var message = MessageOf(new RichBlockList
        {
            Items =
            [
                new RichBlockListItem { Label = "1.", Blocks = [Paragraph("Первый")], Value = 1 },
                new RichBlockListItem { Label = "2.", Blocks = [Paragraph("Второй")], Value = 2 },
            ],
        });
        var expected = string.Join('\n', "1. Первый", "2. Второй");
        Assert.That(message.GetPlainText(), Is.EqualTo(expected));
    }

    [Test]
    public void GetPlainText_WhenTaskList_ShouldRenderCheckboxStates()
    {
        var message = MessageOf(new RichBlockList
        {
            Items =
            [
                new RichBlockListItem { HasCheckbox = true, IsChecked = true, Blocks = [Paragraph("Сделано")] },
                new RichBlockListItem { HasCheckbox = true, Blocks = [Paragraph("Не сделано")] },
            ],
        });
        var expected = string.Join('\n', "[x] Сделано", "[ ] Не сделано");
        Assert.That(message.GetPlainText(), Is.EqualTo(expected));
    }

    [Test]
    public void GetPlainText_WhenListItemHasCheckboxAndLabel_ShouldRenderBoth()
    {
        var message = MessageOf(new RichBlockList
        {
            Items =
            [
                new RichBlockListItem { HasCheckbox = true, IsChecked = true, Label = "3.", Blocks = [Paragraph("Сделано")] },
            ],
        });
        Assert.That(message.GetPlainText(), Is.EqualTo("[x] 3. Сделано"));
    }

    [Test]
    public void GetPlainText_WhenListsAreNested_ShouldReturnAllItems()
    {
        var nestedList = new RichBlockList
        {
            Items =
            [
                new RichBlockListItem { Label = "a.", Blocks = [Paragraph("Ребёнок один")] },
                new RichBlockListItem { Label = "b.", Blocks = [Paragraph("Ребёнок два")] },
            ],
        };
        var message = MessageOf(new RichBlockList
        {
            Items = [new RichBlockListItem { Blocks = [Paragraph("Родитель"), nestedList] }],
        });
        var expected = string.Join('\n', "Родитель", "a. Ребёнок один", "b. Ребёнок два");
        Assert.That(message.GetPlainText(), Is.EqualTo(expected));
    }

    [Test]
    public void GetPlainText_WhenListItemHasMultipleBlocks_ShouldReturnAllOfThem()
    {
        var message = MessageOf(new RichBlockList
        {
            Items =
            [
                new RichBlockListItem
                {
                    Blocks =
                    [
                        Paragraph("Первый абзац пункта"),
                        new RichBlockPreformatted { Text = Plain("код пункта") },
                    ],
                },
            ],
        });
        var expected = string.Join('\n', "Первый абзац пункта", "код пункта");
        Assert.That(message.GetPlainText(), Is.EqualTo(expected));
    }

    [Test]
    public void GetPlainText_WhenListInsideQuotationInsideDetails_ShouldReturnAllLines()
    {
        var message = MessageOf(new RichBlockDetails
        {
            Summary = Plain("Сводка"),
            Blocks =
            [
                new RichBlockBlockQuotation
                {
                    Blocks =
                    [
                        new RichBlockList
                        {
                            Items = [new RichBlockListItem { Label = "1.", Blocks = [Paragraph("Глубокий пункт")] }],
                        },
                    ],
                    Credit = Plain("Автор цитаты"),
                },
            ],
        });
        var expected = string.Join('\n', "Сводка", "1. Глубокий пункт", "Автор цитаты");
        Assert.That(message.GetPlainText(), Is.EqualTo(expected));
    }

    // ---------- tables ----------

    [Test]
    public void GetPlainText_WhenTableWithHeader_ShouldRenderMarkdownTableWithDelimiterRow()
    {
        var message = MessageOf(new RichBlockTable
        {
            Cells =
            [
                [new RichBlockTableCell { Text = Plain("Header 1"), IsHeader = true }, new RichBlockTableCell { Text = Plain("Header 2"), IsHeader = true }],
                [Cell("Value 1"), Cell("Value 2")],
            ],
        });
        var expected = string.Join('\n', "| Header 1 | Header 2 |", "| --- | --- |", "| Value 1 | Value 2 |");
        Assert.That(message.GetPlainText(), Is.EqualTo(expected));
    }

    [Test]
    public void GetPlainText_WhenTableWithCaption_ShouldPutCaptionBeforeRows()
    {
        var message = MessageOf(new RichBlockTable
        {
            Cells = [[Cell("A"), Cell("B")]],
            Caption = Plain("Подпись таблицы"),
            IsBordered = true,
            IsStriped = true,
        });
        var expected = string.Join('\n', "Подпись таблицы", "| A | B |");
        Assert.That(message.GetPlainText(), Is.EqualTo(expected));
    }

    [Test]
    public void GetPlainText_WhenTableWithoutHeader_ShouldNotEmitDelimiterRow()
    {
        var message = MessageOf(new RichBlockTable
        {
            Cells = [[Cell("A"), Cell("B")], [Cell("C"), Cell("D")]],
        });
        var expected = string.Join('\n', "| A | B |", "| C | D |");
        Assert.That(message.GetPlainText(), Is.EqualTo(expected));
    }

    [Test]
    public void GetPlainText_WhenTableHasInvisibleCell_ShouldKeepColumnSeparators()
    {
        var message = MessageOf(new RichBlockTable
        {
            Cells = [[Cell("A"), new RichBlockTableCell(), Cell("C")]],
        });
        Assert.That(message.GetPlainText(), Is.EqualTo("| A |  | C |"));
    }

    [Test]
    public void GetPlainText_WhenTableCellContainsFormatting_ShouldReturnItsPlainText()
    {
        var cell = new RichBlockTableCell
        {
            Text = TextArray(Bold(Plain("42")), new RichTextSuperscript { Text = Plain(" мс") }),
        };
        var message = MessageOf(new RichBlockTable { Cells = [[cell]] });
        Assert.That(message.GetPlainText(), Is.EqualTo("| 42 мс |"));
    }

    [Test]
    public void GetPlainText_WhenTableCellContainsPipe_ShouldEscapeIt()
    {
        var message = MessageOf(new RichBlockTable
        {
            Cells = [[Cell("a|b"), Cell("C")]],
        });
        Assert.That(message.GetPlainText(), Is.EqualTo(@"| a\|b | C |"));
    }

    [Test]
    public void GetPlainText_WhenTableCellContainsLineBreak_ShouldFlattenItToSingleLine()
    {
        var message = MessageOf(new RichBlockTable
        {
            Cells = [[Cell("первая\nвторая"), Cell("C")]],
        });
        Assert.That(message.GetPlainText(), Is.EqualTo("| первая вторая | C |"));
    }

    [Test]
    public void GetPlainText_WhenTableHasNoRows_ShouldReturnOnlyCaption()
    {
        var message = MessageOf(new RichBlockTable
        {
            Cells = [],
            Caption = Plain("Пустая таблица"),
        });
        Assert.That(message.GetPlainText(), Is.EqualTo("Пустая таблица"));
    }

    // ---------- media ----------

    private static IEnumerable<TestCaseData> MediaBlockWithCaptionCases()
    {
        yield return new TestCaseData(
            new RichBlockPhoto { Caption = new RichBlockCaption { Text = Plain("Подпись фото") } },
            "Подпись фото");
        yield return new TestCaseData(
            new RichBlockVideo { HasSpoiler = true, Caption = new RichBlockCaption { Text = Plain("Подпись видео") } },
            "Подпись видео");
        yield return new TestCaseData(
            new RichBlockAnimation { Caption = new RichBlockCaption { Text = Plain("Подпись анимации") } },
            "Подпись анимации");
        yield return new TestCaseData(
            new RichBlockAudio { Caption = new RichBlockCaption { Text = Plain("Подпись аудио") } },
            "Подпись аудио");
        yield return new TestCaseData(
            new RichBlockVoiceNote { Caption = new RichBlockCaption { Text = Plain("Подпись голосового") } },
            "Подпись голосового");
    }

    [TestCaseSource(nameof(MediaBlockWithCaptionCases))]
    public void GetPlainText_WhenMediaBlockWithCaption_ShouldReturnCaptionText(RichBlock block, string expected)
    {
        var message = MessageOf(block);
        Assert.That(message.GetPlainText(), Is.EqualTo(expected));
    }

    [Test]
    public void GetPlainText_WhenPhotoWithCaptionAndCredit_ShouldReturnBoth()
    {
        var message = MessageOf(new RichBlockPhoto
        {
            Caption = new RichBlockCaption { Text = Plain("Подпись фото"), Credit = Plain("Автор фото") },
        });
        var expected = string.Join('\n', "Подпись фото", "Автор фото");
        Assert.That(message.GetPlainText(), Is.EqualTo(expected));
    }

    [Test]
    public void GetPlainText_WhenPhotoWithoutCaption_ShouldContributeNothing()
    {
        var message = MessageOf(Paragraph("A"), new RichBlockPhoto(), Paragraph("B"));
        var expected = string.Join('\n', "A", "B");
        Assert.That(message.GetPlainText(), Is.EqualTo(expected));
    }

    [Test]
    public void GetPlainText_WhenCollageWithMediaCaptionsAndOwnCaption_ShouldReturnAllCaptions()
    {
        var message = MessageOf(new RichBlockCollage
        {
            Blocks =
            [
                new RichBlockPhoto { Caption = new RichBlockCaption { Text = Plain("Фото") } },
                new RichBlockVideo { Caption = new RichBlockCaption { Text = Plain("Видео") } },
            ],
            Caption = new RichBlockCaption { Text = Plain("Коллаж"), Credit = Plain("Автор коллажа") },
        });
        var expected = string.Join('\n', "Фото", "Видео", "Коллаж", "Автор коллажа");
        Assert.That(message.GetPlainText(), Is.EqualTo(expected));
    }

    [Test]
    public void GetPlainText_WhenSlideshowWithCaption_ShouldReturnCaptions()
    {
        var message = MessageOf(new RichBlockSlideshow
        {
            Blocks = [new RichBlockPhoto(), new RichBlockVideo()],
            Caption = new RichBlockCaption { Text = Plain("Слайдшоу") },
        });
        Assert.That(message.GetPlainText(), Is.EqualTo("Слайдшоу"));
    }

    [Test]
    public void GetPlainText_WhenCollageWithoutAnyCaptions_ShouldReturnEmptyString()
    {
        var message = MessageOf(new RichBlockCollage
        {
            Blocks = [new RichBlockPhoto(), new RichBlockVideo()],
        });
        Assert.That(message.GetPlainText(), Is.EqualTo(string.Empty));
    }

    // ---------- whitespace and misc ----------

    [Test]
    public void GetPlainText_WhenRtlMessage_ShouldReturnSameText()
    {
        var message = new RichMessage
        {
            Blocks = [Paragraph("טקסט מימין לשמאל")],
            IsRtl = true,
        };
        Assert.That(message.GetPlainText(), Is.EqualTo("טקסט מימין לשמאל"));
    }

    [Test]
    public void GetPlainText_WhenTextHasSurroundingWhitespace_ShouldTrimOnlyTheEnd()
    {
        var message = MessageOf(Paragraph("  отступ  "));
        Assert.That(message.GetPlainText(), Is.EqualTo("  отступ"));
    }

    [Test]
    public void GetPlainText_WhenMessageStartsWithFormattedMention_ShouldKeepMentionAtTheStart()
    {
        var message = MessageOf(Paragraph(TextArray(Bold(Plain("Пакос")), Plain(", нарисуй кота"))));
        Assert.That(message.GetPlainText(), Is.EqualTo("Пакос, нарисуй кота"));
    }

    [Test]
    public void GetPlainText_WhenMessageStartsWithCommand_ShouldKeepCommandAtTheStart()
    {
        var message = MessageOf(Paragraph("!drawx летающий замок"));
        Assert.That(message.GetPlainText(), Is.EqualTo("!drawx летающий замок"));
    }

    // ---------- full document ----------

    [Test]
    public void GetPlainText_WhenDocumentCombinesAllBlockTypes_ShouldReturnFullPlainText()
    {
        var strikethrough = new RichTextStrikethrough
        {
            Text = TextArray(Plain("зачёркнутым и "), Spoiler(Plain("спойлером"))),
        };
        var quoteParagraph = Paragraph(TextArray(Plain("Цитата с "), Bold(TextArray(Plain("жирным, "), strikethrough))));
        var message = new RichMessage
        {
            Blocks =
            [
                new RichBlockSectionHeading
                {
                    Text = TextArray(Plain("Отчёт за "), new RichTextItalic { Text = Plain("Q1") }),
                    Size = 2,
                },
                Paragraph(TextArray(
                    Plain("Вступление с "),
                    new RichTextUnderline { Text = Plain("подчёркиванием") },
                    Plain(", "),
                    new RichTextMarked { Text = Plain("выделением") },
                    Plain(" и "),
                    new RichTextMathematicalExpression { Expression = "x^2" },
                    Plain("."))),
                new RichBlockBlockQuotation
                {
                    Blocks = [quoteParagraph],
                    Credit = Plain("Классик"),
                },
                new RichBlockList
                {
                    Items =
                    [
                        new RichBlockListItem { HasCheckbox = true, IsChecked = true, Blocks = [Paragraph("Готово")] },
                        new RichBlockListItem
                        {
                            Label = "2.",
                            Blocks = [Paragraph(TextArray(Plain("Пункт с "), new RichTextCode { Text = Plain("кодом") }))],
                        },
                    ],
                },
                new RichBlockTable
                {
                    Caption = Plain("Метрики"),
                    Cells =
                    [
                        [new RichBlockTableCell { Text = Plain("Метрика"), IsHeader = true }, new RichBlockTableCell { Text = Plain("Значение"), IsHeader = true }],
                        [Cell("Скорость"), new RichBlockTableCell { Text = TextArray(Bold(Plain("42")), new RichTextSuperscript { Text = Plain(" мс") }) }],
                    ],
                },
                new RichBlockDetails
                {
                    Summary = TextArray(Plain("Подробности с "), Bold(Plain("жирным"))),
                    Blocks =
                    [
                        new RichBlockSectionHeading { Text = Plain("Внутренний заголовок"), Size = 3 },
                        Paragraph(Spoiler(Plain("Скрытый пункт"))),
                    ],
                },
                new RichBlockDivider(),
                new RichBlockCollage
                {
                    Blocks =
                    [
                        new RichBlockPhoto { Caption = new RichBlockCaption { Text = Plain("Фото") } },
                        new RichBlockVideo { Caption = new RichBlockCaption { Text = Plain("Видео") } },
                    ],
                    Caption = new RichBlockCaption { Text = Plain("Коллаж"), Credit = Plain("Автор") },
                },
                new RichBlockMathematicalExpression { Expression = "E = mc^2" },
                new RichBlockFooter { Text = Plain("Подвал") },
            ],
        };

        var expected = string.Join(
            '\n',
            "Отчёт за Q1",
            "Вступление с подчёркиванием, выделением и x^2.",
            "Цитата с жирным, зачёркнутым и спойлером",
            "Классик",
            "[x] Готово",
            "2. Пункт с кодом",
            "Метрики",
            "| Метрика | Значение |",
            "| --- | --- |",
            "| Скорость | 42 мс |",
            "Подробности с жирным",
            "Внутренний заголовок",
            "Скрытый пункт",
            "Фото",
            "Видео",
            "Коллаж",
            "Автор",
            "E = mc^2",
            "Подвал");

        Assert.That(message.GetPlainText(), Is.EqualTo(expected));
    }
}
