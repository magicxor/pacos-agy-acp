using Markdig;
using Markdig.Extensions.EmphasisExtras;
using Pacos.Services.Markdown.Spoiler;

namespace Pacos.Extensions;

public static class MarkdownPipelineExtensions
{
    public static MarkdownPipelineBuilder UseMdExtensions(this MarkdownPipelineBuilder pipeline)
    {
        return pipeline
            .UseSpoilers()
            .UseAlertBlocks()
            .UseAutoIdentifiers()
            .UseCustomContainers()
            .UseDefinitionLists()
            .UseEmphasisExtras(EmphasisExtraOptions.Strikethrough)
            .UseGridTables()
            .UseMediaLinks()
            .UsePipeTables()
            .UseListExtras()
            .UseTaskLists()
            .UseAutoLinks()
            .UseGenericAttributes(); // Must be last as it is one parser that is modifying other parsers
    }

    /// <summary>
    /// Pipeline for <see cref="Pacos.Services.Markdown.TelegramRichMarkdownRenderer"/>. Only extensions whose
    /// syntax maps onto Telegram Rich Markdown are enabled: spoilers (||…||), alerts (rendered as labeled quotes),
    /// strikethrough (~~…~~) and marked (==…==) emphasis, pipe/grid tables (re-emitted as pipe tables),
    /// task lists (- [x]), footnotes ([^id]), list extras and autolinks (bare URLs are kept unescaped for
    /// Telegram's automatic entity detection). HTML-oriented extensions (auto identifiers, media links,
    /// generic attributes, definition lists) are intentionally left out.
    /// </summary>
    public static MarkdownPipelineBuilder UseRichMdExtensions(this MarkdownPipelineBuilder pipeline)
    {
        return pipeline
            .UseSpoilers()
            .UseAlertBlocks()
            .UseCustomContainers()
            .UseEmphasisExtras(EmphasisExtraOptions.Strikethrough | EmphasisExtraOptions.Marked)
            .UseGridTables()
            .UsePipeTables()
            .UseListExtras()
            .UseTaskLists()
            .UseFootnotes()
            .UseAutoLinks();
    }
}
