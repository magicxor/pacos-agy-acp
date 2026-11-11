using Microsoft.Extensions.Logging.Abstractions;
using Pacos.Services.Markdown;

namespace Pacos.Tests.Unit;

[TestFixture]
[Parallelizable(scope: ParallelScope.All)]
internal sealed class MarkdownConversionServiceTests
{
    private static readonly MarkdownConversionService Service = new(NullLogger<MarkdownConversionService>.Instance);

    [Test]
    [TestCase("| a | b |\n|---|---|\n| 1 | 2 |")]
    [TestCase("- item\n\n  | a | b |\n  |---|---|\n  | 1 | 2 |")]
    [TestCase("> | a | b |\n> |---|---|\n> | 1 | 2 |")]
    [TestCase("+---+---+\n| a | b |\n+===+===+\n| 1 | 2 |\n+---+---+")]
    public void ParseRichMarkdown_WhenTablePresent_ShouldRequireRichMessage(string markdown)
    {
        var document = Service.ParseRichMarkdown(markdown);

        Assert.Multiple(() =>
        {
            Assert.That(document.HasTables, Is.True);
            Assert.That(document.HasMath, Is.False);
            Assert.That(document.RequiresRichMessage, Is.True);
        });
    }

    [Test]
    [TestCase(@"When $a \ne 0$ holds")]
    [TestCase("Energy $$E = mc^2$$ here")]
    [TestCase("$$E = mc^2$$")]
    [TestCase("Text\n\n$$\nE = mc^2\n$$\n\nAfter")]
    [TestCase("```math\nE = mc^2\n```")]
    [TestCase("- item with $x^2$")]
    [TestCase("> quoted $x^2$")]
    public void ParseRichMarkdown_WhenFormulaPresent_ShouldRequireRichMessage(string markdown)
    {
        var document = Service.ParseRichMarkdown(markdown);

        Assert.Multiple(() =>
        {
            Assert.That(document.HasTables, Is.False);
            Assert.That(document.HasMath, Is.True);
            Assert.That(document.RequiresRichMessage, Is.True);
        });
    }

    [Test]
    public void ParseRichMarkdown_WhenTableContainsFormula_ShouldDetectBoth()
    {
        var document = Service.ParseRichMarkdown("| f | v |\n|---|---|\n| $x^2$ | 2 |");

        Assert.Multiple(() =>
        {
            Assert.That(document.HasTables, Is.True);
            Assert.That(document.HasMath, Is.True);
        });
    }

    [Test]
    [TestCase("")]
    [TestCase("Plain **bold** text with a [link](https://example.com)")]
    [TestCase("Money: $5 + $10 = $15, done.")]
    [TestCase("Costs 5$ and 10$ total")]
    [TestCase("US$100–US$200 range")]
    [TestCase("Tickers $AAPL, $TSLA and $MSFT")]
    [TestCase("$HOME/$PATH")]
    [TestCase("`echo $A$` and `a | b`")]
    [TestCase("```\n$x$\n| a | b |\n|---|---|\n```")]
    [TestCase("```python\nprint('$x$')\n```")]
    [TestCase("a | b\nc | d")]
    [TestCase("Use a | b or c || d")]
    public void ParseRichMarkdown_WhenNoTablesOrFormulas_ShouldNotRequireRichMessage(string markdown)
    {
        var document = Service.ParseRichMarkdown(markdown);

        Assert.Multiple(() =>
        {
            Assert.That(document.HasTables, Is.False);
            Assert.That(document.HasMath, Is.False);
            Assert.That(document.RequiresRichMessage, Is.False);
        });
    }

    [Test]
    public void ToTelegramRichMarkdown_ShouldRenderTheParsedDocument()
    {
        var document = Service.ParseRichMarkdown("**bold** and $x^2$");

        Assert.That(document.ToTelegramRichMarkdown(), Is.EqualTo("**bold** and $x^2$"));
    }
}
