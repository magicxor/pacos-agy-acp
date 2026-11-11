using Markdig.Syntax;

namespace Pacos.Services.Markdown;

/// <summary>
/// A document parsed with the rich pipeline, together with the features found in it that only
/// a rich message can display.
/// </summary>
public sealed record RichMarkdownDocument(MarkdownDocument Document, bool HasTables, bool HasMath)
{
    public bool RequiresRichMessage => HasTables || HasMath;

    public string ToTelegramRichMarkdown() => new TelegramRichMarkdownRenderer().Render(Document);
}
