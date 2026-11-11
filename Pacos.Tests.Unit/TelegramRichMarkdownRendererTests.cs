using Markdig;
using Pacos.Extensions;
using Pacos.Services.Markdown;

namespace Pacos.Tests.Unit;

[TestFixture]
[Parallelizable(scope: ParallelScope.All)]
internal sealed class TelegramRichMarkdownRendererTests
{
    private static readonly MarkdownPipeline RichMarkdownPipeline = new MarkdownPipelineBuilder()
        .UseRichMdExtensions()
        .Build();

    private static readonly VerifySettings VerifySettings = new();

    [OneTimeSetUp]
    public void OneTimeSetUp()
    {
        VerifySettings.DisableDiff();
    }

    [Test]
    public void Render_WhenDocumentIsEmpty_ShouldReturnEmptyString()
    {
        Assert.That(Render(string.Empty), Is.EqualTo(string.Empty));
    }

    [Test]
    public void Render_WhenDocumentContainsText_ShouldReturnText()
    {
        Assert.That(Render("Hello World"), Is.EqualTo("Hello World"));
    }

    [Test]
    public void Render_WhenTextContainsFormattingTriggers_ShouldEscapeThem()
    {
        const string standardMarkdown = "Price $5, formula 2 * 2, array[0], a_b, x = y, a | b";
        const string expectedRichMarkdown = @"Price \$5, formula 2 \* 2, array\[0], a\_b, x = y, a | b";

        Assert.That(Render(standardMarkdown), Is.EqualTo(expectedRichMarkdown));
    }

    [Test]
    public void Render_WhenTextContainsDoubledMarkers_ShouldEscapeOnlyDoubledOnes()
    {
        const string standardMarkdown = "a ~~ b == c || d ~ e = f | g";
        const string expectedRichMarkdown = @"a \~\~ b \=\= c |\| d ~ e = f | g";

        Assert.That(Render(standardMarkdown), Is.EqualTo(expectedRichMarkdown));
    }

    [Test]
    public void Render_WhenTextContainsMundanePunctuation_ShouldNotEscapeIt()
    {
        const string standardMarkdown = "Wow. Really?! (Yes) #1 100% a+b, 5>4";
        const string expectedRichMarkdown = "Wow. Really?! (Yes) #1 100% a+b, 5>4";

        Assert.That(Render(standardMarkdown), Is.EqualTo(expectedRichMarkdown));
    }

    [Test]
    [TestCase("Hello, {name}!", "Hello, {name}!")]
    [TestCase("See {#anchor} and {.class} here", "See {#anchor} and {.class} here")]
    [TestCase("Set {key=value} here", "Set {key=value} here")]
    [TestCase(@"Formula \frac{a}{b}", @"Formula \\frac{a}{b}")]
    [TestCase("# Title {#custom-id}", "# Title {#custom-id}")]
    public void Render_WhenTextContainsBraces_ShouldKeepThemAsText(string standardMarkdown, string expectedRichMarkdown)
    {
        Assert.That(Render(standardMarkdown), Is.EqualTo(expectedRichMarkdown));
    }

    [Test]
    [TestCase(":   Definition text")]
    [TestCase("~   Definition text")]
    public void Render_WhenLineLooksLikeDefinition_ShouldKeepPrecedingLine(string definitionLine)
    {
        var richMarkdown = Render(string.Join('\n', "Term", definitionLine));

        Assert.That(richMarkdown, Does.StartWith("Term"));
        Assert.That(richMarkdown, Does.EndWith("Definition text"));
    }

    [Test]
    public void Render_WhenBracketedTextMatchesHeading_ShouldNotTurnItIntoLink()
    {
        var standardMarkdown = string.Join('\n', "# My Heading", string.Empty, "See [My Heading] for details.");
        var expectedRichMarkdown = string.Join('\n', "# My Heading", string.Empty, @"See \[My Heading] for details.");

        Assert.That(Render(standardMarkdown), Is.EqualTo(expectedRichMarkdown));
    }

    [Test]
    [TestCase("**bold**", "**bold**")]
    [TestCase("__bold__", "**bold**")]
    [TestCase("*italic*", "*italic*")]
    [TestCase("_italic_", "_italic_")]
    [TestCase("~~strike~~", "~~strike~~")]
    [TestCase("==marked==", "==marked==")]
    [TestCase("||spoiler||", "||spoiler||")]
    public void Render_WhenSimpleEmphasis_ShouldMapToRichMarkdownEquivalent(string standardMarkdown, string expectedRichMarkdown)
    {
        Assert.That(Render(standardMarkdown), Is.EqualTo(expectedRichMarkdown));
    }

    [Test]
    public void Render_WhenEmphasisIsNested_ShouldPreserveNesting()
    {
        const string standardMarkdown = "**bold _italic ~~struck~~_ and ==marked==**";
        const string expectedRichMarkdown = "**bold _italic ~~struck~~_ and ==marked==**";

        Assert.That(Render(standardMarkdown), Is.EqualTo(expectedRichMarkdown));
    }

    [Test]
    public void Render_WhenBoldIsNestedInBold_ShouldEmitSingleBoldMarkers()
    {
        const string standardMarkdown = "**outer **inner** outer**";
        const string expectedRichMarkdown = "**outer inner outer**";

        Assert.That(Render(standardMarkdown), Is.EqualTo(expectedRichMarkdown));
    }

    [Test]
    public void Render_WhenItalicIsNestedInItalic_ShouldEmitSingleItalicMarkers()
    {
        const string standardMarkdown = "*outer *inner* outer*";
        const string expectedRichMarkdown = "*outer inner outer*";

        Assert.That(Render(standardMarkdown), Is.EqualTo(expectedRichMarkdown));
    }

    [Test]
    public void Render_WhenSpoilerContainsMarkup_ShouldEscapeSpoilerContent()
    {
        const string standardMarkdown = "||*text*||";
        const string expectedRichMarkdown = @"||\*text\*||";

        Assert.That(Render(standardMarkdown), Is.EqualTo(expectedRichMarkdown));
    }

    [Test]
    public void Render_WhenInlineCode_ShouldNotEscapeItsContent()
    {
        const string standardMarkdown = "`var x = a * b;`";
        const string expectedRichMarkdown = "`var x = a * b;`";

        Assert.That(Render(standardMarkdown), Is.EqualTo(expectedRichMarkdown));
    }

    [Test]
    public void Render_WhenInlineCodeContainsBacktick_ShouldUseLongerFence()
    {
        const string standardMarkdown = "`` a`b ``";
        const string expectedRichMarkdown = "``a`b``";

        Assert.That(Render(standardMarkdown), Is.EqualTo(expectedRichMarkdown));
    }

    [Test]
    public void Render_WhenLink_ShouldKeepLinkSyntax()
    {
        const string standardMarkdown = "[text](https://example.com)";
        const string expectedRichMarkdown = "[text](https://example.com)";

        Assert.That(Render(standardMarkdown), Is.EqualTo(expectedRichMarkdown));
    }

    [Test]
    public void Render_WhenLinkUrlContainsParentheses_ShouldEscapeThemInUrl()
    {
        const string standardMarkdown = "[Rust](https://en.wikipedia.org/wiki/Rust_(programming_language))";
        const string expectedRichMarkdown = @"[Rust](https://en.wikipedia.org/wiki/Rust_\(programming_language\))";

        Assert.That(Render(standardMarkdown), Is.EqualTo(expectedRichMarkdown));
    }

    [Test]
    public void Render_WhenBareUrl_ShouldEmitItUnescapedForAutoDetection()
    {
        const string standardMarkdown = "Visit https://example.com/path_with_underscores?a=1&b=2 now";
        const string expectedRichMarkdown = "Visit https://example.com/path_with_underscores?a=1&b=2 now";

        Assert.That(Render(standardMarkdown), Is.EqualTo(expectedRichMarkdown));
    }

    [Test]
    public void Render_WhenImage_ShouldEmitRegularLink()
    {
        const string standardMarkdown = "![Alt text](https://example.com/image.jpg)";
        const string expectedRichMarkdown = "[Alt text](https://example.com/image.jpg)";

        Assert.That(Render(standardMarkdown), Is.EqualTo(expectedRichMarkdown));
    }

    [Test]
    public void Render_WhenImageHasNoAltText_ShouldUseFallbackLabel()
    {
        const string standardMarkdown = "![](https://example.com/image.jpg)";
        const string expectedRichMarkdown = "[Image](https://example.com/image.jpg)";

        Assert.That(Render(standardMarkdown), Is.EqualTo(expectedRichMarkdown));
    }

    [Test]
    [TestCase(1)]
    [TestCase(2)]
    [TestCase(3)]
    [TestCase(4)]
    [TestCase(5)]
    [TestCase(6)]
    public void Render_WhenHeading_ShouldKeepHeadingLevel(int level)
    {
        var hashes = new string('#', level);
        var standardMarkdown = $"{hashes} Heading **bold**";
        var expectedRichMarkdown = $"{hashes} Heading **bold**";

        Assert.That(Render(standardMarkdown), Is.EqualTo(expectedRichMarkdown));
    }

    [Test]
    public void Render_WhenThematicBreak_ShouldEmitDashes()
    {
        Assert.That(Render("***"), Is.EqualTo("---"));
    }

    [Test]
    public void Render_WhenSoftLineBreak_ShouldEmitBackslashHardLineBreak()
    {
        const string standardMarkdown = "line1\nline2";
        const string expectedRichMarkdown = "line1\\\nline2";

        Assert.That(Render(standardMarkdown), Is.EqualTo(expectedRichMarkdown));
    }

    [Test]
    public void Render_WhenSetextHeadingSpansMultipleLines_ShouldCollapseItToSingleLine()
    {
        var standardMarkdown = string.Join('\n', "first", "second", "=====");
        const string expectedRichMarkdown = "# first second";

        Assert.That(Render(standardMarkdown), Is.EqualTo(expectedRichMarkdown));
    }

    [Test]
    public void Render_WhenFootnoteDefinitionSpansMultipleLines_ShouldCollapseItToSingleLine()
    {
        var standardMarkdown = string.Join(
            '\n',
            "Ref[^1].",
            string.Empty,
            "[^1]: line one",
            "    line two");
        var expectedRichMarkdown = string.Join(
            '\n',
            "Ref[^1].",
            string.Empty,
            "[^1]: line one line two");

        Assert.That(Render(standardMarkdown), Is.EqualTo(expectedRichMarkdown));
    }

    [Test]
    public void Render_WhenQuoteHasMultipleParagraphs_ShouldSeparateThemWithBareQuoteLine()
    {
        var standardMarkdown = string.Join('\n', "> p1", ">", "> p2");
        var expectedRichMarkdown = string.Join('\n', ">p1", ">", ">p2");

        Assert.That(Render(standardMarkdown), Is.EqualTo(expectedRichMarkdown));
    }

    [Test]
    public void Render_WhenQuoteIsNested_ShouldDoubleQuoteMarker()
    {
        var standardMarkdown = string.Join('\n', "> outer", "> > inner");
        var expectedRichMarkdown = string.Join('\n', ">outer", ">", ">>inner");

        Assert.That(Render(standardMarkdown), Is.EqualTo(expectedRichMarkdown));
    }

    [Test]
    [TestCase("NOTE", "ℹ️ Note")]
    [TestCase("TIP", "💡 Tip")]
    [TestCase("IMPORTANT", "❗ Important")]
    [TestCase("WARNING", "⚠️ Warning")]
    [TestCase("CAUTION", "🛑 Caution")]
    public void Render_WhenAlertBlock_ShouldRenderLabelOnItsOwnQuoteLine(string kind, string expectedLabel)
    {
        var standardMarkdown = string.Join('\n', $"> [!{kind}]", "> Useful info.");
        var expectedRichMarkdown = string.Join('\n', $">**{expectedLabel}**", ">", ">Useful info.");

        Assert.That(Render(standardMarkdown), Is.EqualTo(expectedRichMarkdown));
    }

    [Test]
    public void Render_WhenTaskList_ShouldKeepTaskListSyntax()
    {
        var standardMarkdown = string.Join('\n', "- [ ] todo", "- [x] **done** with `code`");
        var expectedRichMarkdown = string.Join('\n', "- [ ] todo", "- [x] **done** with `code`");

        Assert.That(Render(standardMarkdown), Is.EqualTo(expectedRichMarkdown));
    }

    [Test]
    public void Render_WhenOrderedListContinuesAfterParagraph_ShouldPreserveNumbering()
    {
        var standardMarkdown = string.Join('\n', "1. one", "2. two", string.Empty, "interruption", string.Empty, "3. three", "4. four");
        var expectedRichMarkdown = string.Join('\n', "1. one", "2. two", string.Empty, "interruption", string.Empty, "3. three", "4. four");

        Assert.That(Render(standardMarkdown), Is.EqualTo(expectedRichMarkdown));
    }

    [Test]
    public void Render_WhenNestedOrderedListStartsAtFive_ShouldPreserveStartNumber()
    {
        var standardMarkdown = string.Join('\n', "1. outer", string.Empty, "   5. nested five", "   6. nested six");
        var expectedRichMarkdown = string.Join('\n', "1. outer", string.Empty, "   5. nested five", "   6. nested six");

        Assert.That(Render(standardMarkdown), Is.EqualTo(expectedRichMarkdown));
    }

    [Test]
    public void Render_WhenListItemContainsMultipleParagraphs_ShouldIndentContinuation()
    {
        var standardMarkdown = string.Join('\n', "- para one", string.Empty, "  para two");
        var expectedRichMarkdown = string.Join('\n', "- para one", string.Empty, "  para two");

        Assert.That(Render(standardMarkdown), Is.EqualTo(expectedRichMarkdown));
    }

    [Test]
    public void Render_WhenListItemContainsCodeBlock_ShouldIndentFencedBlock()
    {
        var standardMarkdown = string.Join('\n', "- item:", string.Empty, "  ```py", "  x = 1", "  ```");
        var expectedRichMarkdown = string.Join('\n', "- item:", string.Empty, "  ```py", "  x = 1", "  ```");

        Assert.That(Render(standardMarkdown), Is.EqualTo(expectedRichMarkdown));
    }

    [Test]
    public void Render_WhenPipeTable_ShouldEmitAlignedPipeTable()
    {
        var standardMarkdown = string.Join(
            '\n',
            "| L | C | R | D |",
            "|:--|:-:|--:|---|",
            "| 1 | 2 | 3 | 4 |");
        var expectedRichMarkdown = string.Join(
            '\n',
            "| L   | C   | R   | D   |",
            "|:----|:---:|----:|-----|",
            "| 1   | 2   | 3   | 4   |");

        Assert.That(Render(standardMarkdown), Is.EqualTo(expectedRichMarkdown));
    }

    [Test]
    public void Render_WhenTableCellContainsPipe_ShouldEscapePipeInCell()
    {
        var standardMarkdown = string.Join(
            '\n',
            "| H |",
            "|---|",
            @"| a\|b |",
            "| `x|y` |");
        var expectedRichMarkdown = string.Join(
            '\n',
            "| H      |",
            "|--------|",
            @"| a\|b   |",
            @"| `x\|y` |");

        Assert.That(Render(standardMarkdown), Is.EqualTo(expectedRichMarkdown));
    }

    [Test]
    public void Render_WhenFootnotes_ShouldEmitFootnoteReferencesAndDefinitions()
    {
        var standardMarkdown = string.Join(
            '\n',
            "Text with note[^1] and named[^note].",
            string.Empty,
            "[^1]: The first footnote.",
            "[^note]: The named footnote with **bold**.");
        var expectedRichMarkdown = string.Join(
            '\n',
            "Text with note[^1] and named[^note].",
            string.Empty,
            "[^1]: The first footnote.",
            "[^note]: The named footnote with **bold**.");

        Assert.That(Render(standardMarkdown), Is.EqualTo(expectedRichMarkdown));
    }

    [Test]
    public void Render_WhenHtmlInline_ShouldShowTagsAsText()
    {
        const string standardMarkdown = "Tags like <b>bold</b> stay visible";
        const string expectedRichMarkdown = @"Tags like \<b>bold\</b> stay visible";

        Assert.That(Render(standardMarkdown), Is.EqualTo(expectedRichMarkdown));
    }

    [Test]
    public void Render_WhenHtmlEntities_ShouldRenderTranscodedText()
    {
        const string standardMarkdown = "AT&amp;T &lt;tag&gt; &copy; 2026";
        const string expectedRichMarkdown = @"AT&T \<tag> © 2026";

        Assert.That(Render(standardMarkdown), Is.EqualTo(expectedRichMarkdown));
    }

    [Test]
    public void Render_WhenCodeBlockHasLanguage_ShouldKeepLanguageAndRawContent()
    {
        var standardMarkdown = string.Join('\n', "```python", "print('a * b')", "```");
        var expectedRichMarkdown = string.Join('\n', "```python", "print('a * b')", "```");

        Assert.That(Render(standardMarkdown), Is.EqualTo(expectedRichMarkdown));
    }

    [Test]
    public void Render_WhenCodeBlockContainsBacktickFence_ShouldUseLongerFence()
    {
        var standardMarkdown = string.Join('\n', "````", "```", "nested fence", "```", "````");
        var expectedRichMarkdown = string.Join('\n', "````", "```", "nested fence", "```", "````");

        Assert.That(Render(standardMarkdown), Is.EqualTo(expectedRichMarkdown));
    }

    [Test]
    public void Render_WhenIndentedCodeBlock_ShouldEmitFencedBlock()
    {
        const string standardMarkdown = "    var x = 1;";
        var expectedRichMarkdown = string.Join('\n', "```", "var x = 1;", "```");

        Assert.That(Render(standardMarkdown), Is.EqualTo(expectedRichMarkdown));
    }

    [Test]
    [TestCase(@"When $a \ne 0$ holds", @"When $a \ne 0$ holds")]
    [TestCase("Product $a_1 * b_2$ here", "Product $a_1 * b_2$ here")]
    [TestCase("Energy $$E = mc^2$$ here", "Energy $E = mc^2$ here")]
    public void Render_WhenInlineFormula_ShouldEmitItUnescaped(string standardMarkdown, string expectedRichMarkdown)
    {
        Assert.That(Render(standardMarkdown), Is.EqualTo(expectedRichMarkdown));
    }

    [Test]
    public void Render_WhenFootnoteDefinitionContainsFormulaBlock_ShouldEmitInlineFormula()
    {
        var standardMarkdown = string.Join(
            '\n',
            "Ref[^1].",
            string.Empty,
            "[^1]: The identity",
            string.Empty,
            "    $$",
            "    a^2 +",
            "    b^2",
            "    $$");
        var expectedRichMarkdown = string.Join(
            '\n',
            "Ref[^1].",
            string.Empty,
            "[^1]: The identity $a^2 + b^2$");

        Assert.That(Render(standardMarkdown), Is.EqualTo(expectedRichMarkdown));
    }

    [Test]
    [TestCase("Money: $5 + $10 = $15", @"Money: \$5 + \$10 = \$15")]
    [TestCase("5$ and 10$", @"5\$ and 10\$")]
    [TestCase("$HOME/$PATH", @"\$HOME/\$PATH")]
    public void Render_WhenDollarsAreNotFormulaDelimiters_ShouldEscapeThem(string standardMarkdown, string expectedRichMarkdown)
    {
        Assert.That(Render(standardMarkdown), Is.EqualTo(expectedRichMarkdown));
    }

    [Test]
    public void Render_WhenFormulaBlock_ShouldEmitMathFence()
    {
        var standardMarkdown = string.Join('\n', "$$", @"a &= b \\", "c &= d", "$$");
        var expectedRichMarkdown = string.Join('\n', "```math", @"a &= b \\", "c &= d", "```");

        Assert.That(Render(standardMarkdown), Is.EqualTo(expectedRichMarkdown));
    }

    [Test]
    public void Render_WhenParagraphIsSingleDisplayFormula_ShouldEmitMathFence()
    {
        const string standardMarkdown = "$$E = mc^2$$";
        var expectedRichMarkdown = string.Join('\n', "```math", "E = mc^2", "```");

        Assert.That(Render(standardMarkdown), Is.EqualTo(expectedRichMarkdown));
    }

    [Test]
    public void Render_WhenListItemContainsFormulaBlock_ShouldIndentMathFence()
    {
        var standardMarkdown = string.Join('\n', "- item:", string.Empty, "  $$", "  E = mc^2", "  $$");
        var expectedRichMarkdown = string.Join('\n', "- item:", string.Empty, "  ```math", "  E = mc^2", "  ```");

        Assert.That(Render(standardMarkdown), Is.EqualTo(expectedRichMarkdown));
    }

    [Test]
    public void Render_WhenTableCellContainsFormula_ShouldKeepItInline()
    {
        var standardMarkdown = string.Join(
            '\n',
            "| F |",
            "|---|",
            "| $x^2$ |");
        var expectedRichMarkdown = string.Join(
            '\n',
            "| F     |",
            "|-------|",
            "| $x^2$ |");

        Assert.That(Render(standardMarkdown), Is.EqualTo(expectedRichMarkdown));
    }

    [Test]
    [TestCase("checkbox_test.md")]
    [TestCase("image_test.md")]
    [TestCase("table_test.md")]
    [TestCase("test_all_en.md")]
    [TestCase("test_all_ru.md")]
    [TestCase("quote_bug.md")]
    [TestCase("complex_list.md")]
    [TestCase("nested_list_blocks.md")]
    [TestCase("task_list_formatting.md")]
    [TestCase("nested_lists.md")]
    [TestCase("code_blocks_list.md")]
    [TestCase("code_blocks.md")]
    [TestCase("html_tags.md")]
    [TestCase("rich_features.md")]
    [TestCase("math_test.md")]
    public async Task Render_ShouldReturnValidRichMarkdown(string fileName)
    {
        var standardMarkdown = await File.ReadAllTextAsync(Path.Combine("Files", fileName));

        var actualRichMarkdown = Render(standardMarkdown);

        await Verify(actualRichMarkdown, VerifySettings).UseParameters(fileName);
    }

    private static string Render(string standardMarkdown)
    {
        var document = Markdown.Parse(standardMarkdown, RichMarkdownPipeline);
        return new TelegramRichMarkdownRenderer().Render(document);
    }
}
