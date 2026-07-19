using Markdig;
using Markdig.Extensions.EmphasisExtras;
using Pacos.Extensions;
using Pacos.Services;
using Pacos.Services.Markdown;

namespace Pacos.Tests.Unit;

[TestFixture]
[Parallelizable(scope: ParallelScope.All)]
internal sealed class TelegramMarkdownRendererTests
{
    private static readonly MarkdownPipeline MarkdownPipeline = new MarkdownPipelineBuilder()
        .UseMdExtensions()
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
        const string standardMarkdown = "";
        const string expectedTelegramMarkdown = "";

        var standardMarkdownDoc = Markdown.Parse(standardMarkdown, MarkdownPipeline);
        var actualTelegramMarkdown = new TelegramMarkdownRenderer().Render(standardMarkdownDoc);
        Assert.That(actualTelegramMarkdown, Is.EqualTo(expectedTelegramMarkdown));
    }

    [Test]
    public void Render_WhenDocumentContainsText_ShouldReturnText()
    {
        const string standardMarkdown = "Hello World";
        const string expectedTelegramMarkdown = "Hello World";

        var standardMarkdownDoc = Markdown.Parse(standardMarkdown, MarkdownPipeline);
        var actualTelegramMarkdown = new TelegramMarkdownRenderer().Render(standardMarkdownDoc);
        Assert.That(actualTelegramMarkdown, Is.EqualTo(expectedTelegramMarkdown));
    }

    [Test]
    public void Render_WhenDocumentContainsHtmlEntities_ShouldRenderTranscodedText()
    {
        const string standardMarkdown = "AT&amp;T &lt;tag&gt; &copy; 2026";
        const string expectedTelegramMarkdown = @"AT&T <tag\> © 2026";

        var standardMarkdownDoc = Markdown.Parse(standardMarkdown, MarkdownPipeline);
        var actualTelegramMarkdown = new TelegramMarkdownRenderer().Render(standardMarkdownDoc);
        Assert.That(actualTelegramMarkdown, Is.EqualTo(expectedTelegramMarkdown));
    }

    [Test]
    public void Render_WhenHeadingContainsBold_ShouldNotNestBoldMarkers()
    {
        const string standardMarkdown = "# Head **bold** tail";
        const string expectedTelegramMarkdown = "*Head bold tail*";

        var standardMarkdownDoc = Markdown.Parse(standardMarkdown, MarkdownPipeline);
        var actualTelegramMarkdown = new TelegramMarkdownRenderer().Render(standardMarkdownDoc);
        Assert.That(actualTelegramMarkdown, Is.EqualTo(expectedTelegramMarkdown));
    }

    [Test]
    public void Render_WhenBoldIsNestedInBold_ShouldEmitSingleBoldMarkers()
    {
        const string standardMarkdown = "**outer **inner** outer**";
        const string expectedTelegramMarkdown = "*outer inner outer*";

        var standardMarkdownDoc = Markdown.Parse(standardMarkdown, MarkdownPipeline);
        var actualTelegramMarkdown = new TelegramMarkdownRenderer().Render(standardMarkdownDoc);
        Assert.That(actualTelegramMarkdown, Is.EqualTo(expectedTelegramMarkdown));
    }

    [Test]
    public void Render_WhenItalicIsNestedInItalic_ShouldEmitSingleItalicMarkers()
    {
        const string standardMarkdown = "*outer *inner* outer*";
        const string expectedTelegramMarkdown = "\u200B_\u200Bouter inner outer\u200B_\u200B";

        var standardMarkdownDoc = Markdown.Parse(standardMarkdown, MarkdownPipeline);
        var actualTelegramMarkdown = new TelegramMarkdownRenderer().Render(standardMarkdownDoc);
        Assert.That(actualTelegramMarkdown, Is.EqualTo(expectedTelegramMarkdown));
    }

    [Test]
    public void Render_WhenEmphasisTypeIsUnknown_ShouldKeepContentWithoutMarkers()
    {
        // The production pipeline has no superscript, so build one that yields an EmphasisInline the renderer does not map
        var pipelineWithSuperscript = new MarkdownPipelineBuilder()
            .UseEmphasisExtras(EmphasisExtraOptions.Superscript)
            .Build();
        const string standardMarkdown = "E = mc^2^ formula";
        const string expectedTelegramMarkdown = @"E \= mc2 formula";

        var standardMarkdownDoc = Markdown.Parse(standardMarkdown, pipelineWithSuperscript);
        var actualTelegramMarkdown = new TelegramMarkdownRenderer().Render(standardMarkdownDoc);
        Assert.That(actualTelegramMarkdown, Is.EqualTo(expectedTelegramMarkdown));
    }

    [Test]
    [TestCase("NOTE", "ℹ️ Note")]
    [TestCase("TIP", "💡 Tip")]
    [TestCase("IMPORTANT", "❗ Important")]
    [TestCase("WARNING", "⚠️ Warning")]
    [TestCase("CAUTION", "🛑 Caution")]
    public void Render_WhenAlertBlock_ShouldRenderLabelInsideQuote(string kind, string expectedLabel)
    {
        var standardMarkdown = string.Join('\n', $"> [!{kind}]", "> Useful info.");
        var expectedTelegramMarkdown = string.Join(
            Environment.NewLine,
            $">*{expectedLabel}*",
            @">Useful info\.");

        var standardMarkdownDoc = Markdown.Parse(standardMarkdown, MarkdownPipeline);
        var actualTelegramMarkdown = new TelegramMarkdownRenderer().Render(standardMarkdownDoc);
        Assert.That(actualTelegramMarkdown, Is.EqualTo(expectedTelegramMarkdown));
    }

    [Test]
    public void Render_WhenTaskListItem_ShouldHaveSingleSpaceAfterCheckbox()
    {
        var standardMarkdown = string.Join('\n', "- [ ] todo", "- [x] done");
        var expectedTelegramMarkdown = string.Join(
            Environment.NewLine,
            @"\- \[ \] todo",
            @"\- \[x\] done");

        var standardMarkdownDoc = Markdown.Parse(standardMarkdown, MarkdownPipeline);
        var actualTelegramMarkdown = new TelegramMarkdownRenderer().Render(standardMarkdownDoc);
        Assert.That(actualTelegramMarkdown, Is.EqualTo(expectedTelegramMarkdown));
    }

    [Test]
    public void Render_WhenOrderedListContinuesAfterParagraph_ShouldPreserveNumbering()
    {
        var standardMarkdown = string.Join('\n', "1. one", "2. two", string.Empty, "interruption", string.Empty, "3. three", "4. four");
        var expectedTelegramMarkdown = string.Join(
            Environment.NewLine,
            @"1\. one",
            @"2\. two",
            string.Empty,
            "interruption",
            string.Empty,
            @"3\. three",
            @"4\. four");

        var standardMarkdownDoc = Markdown.Parse(standardMarkdown, MarkdownPipeline);
        var actualTelegramMarkdown = new TelegramMarkdownRenderer().Render(standardMarkdownDoc);
        Assert.That(actualTelegramMarkdown, Is.EqualTo(expectedTelegramMarkdown));
    }

    [Test]
    public void Render_WhenNestedOrderedListStartsAtFive_ShouldPreserveStartNumber()
    {
        var standardMarkdown = string.Join('\n', "1. outer", string.Empty, "   5. nested five", "   6. nested six");
        var expectedTelegramMarkdown = string.Join(
            Environment.NewLine,
            @"1\. outer",
            @"  5\. nested five",
            @"  6\. nested six");

        var standardMarkdownDoc = Markdown.Parse(standardMarkdown, MarkdownPipeline);
        var actualTelegramMarkdown = new TelegramMarkdownRenderer().Render(standardMarkdownDoc);
        Assert.That(actualTelegramMarkdown, Is.EqualTo(expectedTelegramMarkdown));
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
    public async Task Render_ShouldReturnValidMarkdown(string fileName)
    {
        var standardMarkdown = await File.ReadAllTextAsync(Path.Combine("Files", fileName));

        var standardMarkdownDoc = Markdown.Parse(standardMarkdown, MarkdownPipeline);
        var actualTelegramMarkdown = new TelegramMarkdownRenderer().Render(standardMarkdownDoc);

        await Verify(actualTelegramMarkdown, VerifySettings).UseParameters(fileName);
    }
}
