using Markdig;
using Markdig.Extensions.Mathematics;
using Markdig.Extensions.Tables;
using Markdig.Syntax;
using Pacos.Extensions;

namespace Pacos.Services.Markdown;

public sealed class MarkdownConversionService
{
    private static readonly MarkdownPipeline MarkdownPipeline = new MarkdownPipelineBuilder()
        .UseMdExtensions()
        .Build();
    private static readonly MarkdownPipeline RichMarkdownPipeline = new MarkdownPipelineBuilder()
        .UseRichMdExtensions()
        .Build();
    private readonly ILogger<MarkdownConversionService> _logger;

    public MarkdownConversionService(
        ILogger<MarkdownConversionService> logger)
    {
        _logger = logger;
    }

    public string ConvertToTelegramMarkdown(string normalMarkdown)
    {
        _logger.LogDebug("Converting normal markdown to Telegram markdown: {NormalMarkdown}", normalMarkdown);
        var document = Markdig.Markdown.Parse(normalMarkdown, MarkdownPipeline);
        return new TelegramMarkdownRenderer().Render(document);
    }

    public RichMarkdownDocument ParseRichMarkdown(string normalMarkdown)
    {
        _logger.LogDebug("Parsing normal markdown for a Telegram rich message: {NormalMarkdown}", normalMarkdown);
        var document = Markdig.Markdown.Parse(normalMarkdown, RichMarkdownPipeline);

        var hasTables = false;
        var hasMath = false;
        foreach (var node in document.Descendants())
        {
            hasTables = hasTables || node is Table;
            hasMath = hasMath || IsMath(node);

            if (hasTables && hasMath)
            {
                break;
            }
        }

        return new RichMarkdownDocument(document, hasTables, hasMath);
    }

    // A ```math code block counts as well: Rich Markdown renders it as a formula
    private static bool IsMath(MarkdownObject node)
        => node is MathInline or MathBlock or FencedCodeBlock { Info: TelegramRichMarkdownRenderer.MathBlockInfo };
}
