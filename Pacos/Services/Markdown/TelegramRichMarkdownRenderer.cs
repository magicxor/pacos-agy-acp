using System.Globalization;
using System.Text;
using Markdig.Extensions.Alerts;
using Markdig.Extensions.Footnotes;
using Markdig.Extensions.Tables;
using Markdig.Extensions.TaskLists;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using Pacos.Extensions;
using Pacos.Services.Markdown.Spoiler;

namespace Pacos.Services.Markdown;

/// <summary>
/// Renders a Markdig AST into Telegram Rich Markdown — the GitHub-flavored dialect accepted by the
/// <c>markdown</c> field of rich messages. Unlike MarkdownV2, only characters that could start
/// unintended formatting are escaped, so the output stays close to regular Markdown.
/// </summary>
public sealed class TelegramRichMarkdownRenderer
{
    // Soft breaks are emitted as GFM hard breaks to preserve the line structure of the source
    // text; a bare newline would be collapsed into a space. The backslash form is used instead
    // of two trailing spaces because trailing whitespace does not survive editors and linters
    // (e.g. the .editorconfig of this very repository trims it in the snapshot files).
    private const string HardLineBreak = "\\\n";
    private const int MinTableColumnWidth = 3;

    private readonly StringBuilder _output = new();
    private readonly HashSet<string> _activeMarkers = new(StringComparer.Ordinal);

    private bool InTableCell { get; init; }

    private bool InSingleLine { get; init; }

    public string Render(MarkdownDocument document)
    {
        _output.Clear();
        _activeMarkers.Clear();
        foreach (var block in document)
        {
            RenderBlock(block);
        }
        return _output.ToString().Trim();
    }

    private void RenderBlock(Block block)
    {
        switch (block)
        {
            case HeadingBlock heading:
                RenderHeading(heading);
                break;
            case ParagraphBlock paragraph:
                RenderParagraph(paragraph);
                break;
            case ListBlock list:
                RenderList(list);
                break;
            case AlertBlock alert:
                RenderAlert(alert);
                break;
            case QuoteBlock quote:
                RenderQuote(quote);
                break;
            case CodeBlock code:
                RenderCodeBlock(code);
                break;
            case Table table:
                RenderTable(table);
                break;
            case ThematicBreakBlock:
                _output.Append("---\n\n");
                break;
            case FootnoteGroup footnoteGroup:
                RenderFootnoteGroup(footnoteGroup);
                break;
            case HtmlBlock html:
                RenderHtmlBlock(html);
                break;
            case ContainerBlock container:
                foreach (var child in container)
                {
                    RenderBlock(child);
                }
                break;
            default:
                break;
        }
    }

    private void RenderHeading(HeadingBlock heading)
    {
        // A multi-line setext heading must collapse to one line to stay a valid ATX heading
        var text = FlattenToSingleLine(BuildInlinesText(heading.Inline, singleLine: true));
        _output.Append('#', heading.Level).Append(' ').Append(text).Append("\n\n");
    }

    private void RenderParagraph(ParagraphBlock paragraph)
    {
        RenderInlines(paragraph.Inline);
        _output.Append("\n\n");
    }

    private void RenderList(ListBlock list)
    {
        _output.Append(BuildListText(list)).Append("\n\n");
    }

    private string BuildListText(ListBlock list)
    {
        var builder = new StringBuilder();
        var index = GetOrderedStart(list);
        foreach (var item in list.OfType<ListItemBlock>())
        {
            if (builder.Length > 0)
            {
                builder.Append(list.IsLoose ? "\n\n" : "\n");
            }
            builder.Append(BuildListItemText(list, item, index));
            index++;
        }
        return builder.ToString();
    }

    private string BuildListItemText(ListBlock list, ListItemBlock item, int index)
    {
        var marker = list.IsOrdered
            ? string.Create(CultureInfo.InvariantCulture, $"{index}. ")
            : "- ";

        var isTaskItem = TryGetTaskListCheckbox(list, item, out var isChecked, out var taskParagraph);
        var content = new StringBuilder();
        if (isTaskItem)
        {
            content.Append(isChecked ? "[x] " : "[ ] ");
        }

        Block? previousBlock = null;
        foreach (var child in item)
        {
            var text = isTaskItem && ReferenceEquals(child, taskParagraph)
                ? BuildTaskParagraphText((ParagraphBlock)child)
                : BuildBlockText(child);
            if (text.Length == 0)
            {
                continue;
            }

            if (previousBlock is not null)
            {
                var keepAdjacent = previousBlock is ParagraphBlock && child is ListBlock && !list.IsLoose;
                content.Append(keepAdjacent ? "\n" : "\n\n");
            }
            content.Append(text);
            previousBlock = child;
        }

        if (content.Length == 0)
        {
            return marker.TrimEnd();
        }

        return PrefixLines(content.ToString(), marker, new string(' ', marker.Length));
    }

    private string BuildTaskParagraphText(ParagraphBlock paragraph)
    {
        if (paragraph.Inline is null)
        {
            return string.Empty;
        }

        var renderer = CreateChild();
        foreach (var inline in paragraph.Inline)
        {
            if (inline is TaskList)
            {
                continue;
            }
            renderer.RenderInline(inline);
        }
        // The source text after the checkbox starts with its own space; the list item marker already ends with one
        return renderer._output.ToString().TrimStart(' ');
    }

    private static bool TryGetTaskListCheckbox(ListBlock list, ListItemBlock item, out bool isChecked, out ParagraphBlock? taskParagraph)
    {
        isChecked = false;
        taskParagraph = null;

        if (list.IsOrdered || item.Count == 0 || item[0] is not ParagraphBlock firstParagraph || firstParagraph.Inline?.FirstChild is not TaskList taskList)
        {
            return false;
        }

        isChecked = taskList.Checked;
        taskParagraph = firstParagraph;
        return true;
    }

    private void RenderAlert(AlertBlock alert)
    {
        var kind = alert.Kind.ToString();
        var label = kind.ToUpperInvariant() switch
        {
            "NOTE" => "ℹ️ Note",
            "TIP" => "💡 Tip",
            "IMPORTANT" => "❗ Important",
            "WARNING" => "⚠️ Warning",
            "CAUTION" => "🛑 Caution",
            _ => kind,
        };

        // Telegram joins adjacent quote lines, so a bare ">" line keeps the label on its own line
        _output.Append(">**").Append(EscapeText(label)).Append("**\n>\n");
        _output.Append(BuildQuoteText(alert)).Append("\n\n");
    }

    private void RenderQuote(QuoteBlock quote)
    {
        _output.Append(BuildQuoteText(quote)).Append("\n\n");
    }

    private string BuildQuoteText(QuoteBlock quote)
    {
        var blockTexts = quote
            .Select(block => BuildBlockText(block))
            .Where(static text => text.Length > 0);
        var joined = string.Join("\n\n", blockTexts);

        // A blank line inside a quotation is expressed as a bare ">" line
        var lines = joined.Split('\n');
        var builder = new StringBuilder();
        foreach (var line in lines)
        {
            if (builder.Length > 0)
            {
                builder.Append('\n');
            }
            builder.Append('>').Append(line);
        }
        return builder.ToString();
    }

    private void RenderCodeBlock(CodeBlock code)
    {
        // Markdig keeps the source line endings, so CRLF input would leak '\r' into the output
        var content = (code.Lines.ToString() ?? string.Empty).ReplaceLineEndings("\n");
        var fence = new string('`', Math.Max(3, content.LongestRunOf('`') + 1));

        _output.Append(fence);
        if (code is FencedCodeBlock { Info.Length: > 0 } fenced)
        {
            _output.Append(fenced.Info);
        }
        _output.Append('\n');
        if (content.Length > 0)
        {
            _output.Append(content).Append('\n');
        }
        _output.Append(fence).Append("\n\n");
    }

    private void RenderTable(Table table)
    {
        var rows = new List<(bool IsHeader, List<string> Cells)>();
        foreach (var row in table.OfType<TableRow>())
        {
            var cells = row.OfType<TableCell>().Select(BuildTableCellText).ToList();
            rows.Add((row.IsHeader, cells));
        }

        if (rows.Count == 0)
        {
            return;
        }

        var columnCount = Math.Max(table.ColumnDefinitions.Count, rows.Max(static row => row.Cells.Count));
        if (columnCount == 0)
        {
            return;
        }

        var widths = new int[columnCount];
        for (var column = 0; column < columnCount; column++)
        {
            var maxCellWidth = rows.Max(row => column < row.Cells.Count ? row.Cells[column].Length : 0);
            widths[column] = Math.Max(MinTableColumnWidth, maxCellWidth);
        }

        var hasHeader = rows[0].IsHeader;
        List<string> headerCells = hasHeader ? rows[0].Cells : [];
        AppendTableRow(headerCells, widths);
        AppendTableDelimiterRow(table.ColumnDefinitions, widths);
        foreach (var (_, cells) in hasHeader ? rows.Skip(1) : rows)
        {
            AppendTableRow(cells, widths);
        }
        _output.Append('\n');
    }

    private void AppendTableRow(List<string> cells, int[] widths)
    {
        _output.Append('|');
        for (var column = 0; column < widths.Length; column++)
        {
            var cell = column < cells.Count ? cells[column] : string.Empty;
            _output.Append(' ').Append(cell.PadRight(widths[column])).Append(" |");
        }
        _output.Append('\n');
    }

    private void AppendTableDelimiterRow(List<TableColumnDefinition> columnDefinitions, int[] widths)
    {
        _output.Append('|');
        for (var column = 0; column < widths.Length; column++)
        {
            var alignment = column < columnDefinitions.Count ? columnDefinitions[column].Alignment : null;
            _output.Append(BuildTableDelimiterCell(alignment, widths[column])).Append('|');
        }
        _output.Append('\n');
    }

    private static string BuildTableDelimiterCell(TableColumnAlign? alignment, int width)
    {
        // The delimiter spans the cell padding as well to line up with "| cell |" rows
        var cell = new char[width + 2];
        Array.Fill(cell, '-');
        if (alignment is TableColumnAlign.Left or TableColumnAlign.Center)
        {
            cell[0] = ':';
        }
        if (alignment is TableColumnAlign.Right or TableColumnAlign.Center)
        {
            cell[^1] = ':';
        }
        return new string(cell);
    }

    private string BuildTableCellText(TableCell cell)
    {
        var renderer = new TelegramRichMarkdownRenderer { InTableCell = true, InSingleLine = true };
        foreach (var block in cell)
        {
            renderer.RenderBlock(block);
        }

        // Rich Markdown table cells support only inline formatting, so cell content is flattened to one line
        var lines = renderer._output.ToString()
            .Split('\n')
            .Select(static line => line.TrimEnd())
            .Where(static line => line.Length > 0);
        return string.Join(' ', lines).Trim();
    }

    private void RenderFootnoteGroup(FootnoteGroup footnoteGroup)
    {
        foreach (var footnote in footnoteGroup.OfType<Footnote>())
        {
            var blockTexts = footnote
                .Select(block => BuildBlockText(block, singleLine: true))
                .Where(static text => text.Length > 0);
            var content = FlattenToSingleLine(string.Join(' ', blockTexts));
            _output.Append("[^").Append(GetFootnoteLabel(footnote)).Append("]: ").Append(content).Append('\n');
        }
        _output.Append('\n');
    }

    private static string GetFootnoteLabel(Footnote footnote)
    {
        // Markdig stores footnote labels with the leading caret (e.g. "^note")
        var label = footnote.Label ?? footnote.Order.ToString(CultureInfo.InvariantCulture);
        return label.StartsWith('^') ? label[1..] : label;
    }

    private void RenderHtmlBlock(HtmlBlock html)
    {
        // Tags are shown verbatim: LLMs writing HTML usually mean it to be read, not rendered
        var content = (html.Lines.ToString() ?? string.Empty).ReplaceLineEndings("\n");
        _output.Append(EscapeText(content)).Append("\n\n");
    }

    private void RenderInlines(ContainerInline? container)
    {
        if (container is null)
        {
            return;
        }

        foreach (var inline in container)
        {
            RenderInline(inline);
        }
    }

    private void RenderInline(Inline inline)
    {
        switch (inline)
        {
            case LiteralInline literal:
                _output.Append(EscapeText(literal.Content.ToString()));
                break;
            case EmphasisInline emphasis:
                RenderEmphasis(emphasis);
                break;
            case LinkInline link:
                RenderLink(link);
                break;
            case CodeInline code:
                RenderCodeInline(code);
                break;
            case AutolinkInline autolink:
                // Left unescaped so Telegram's automatic entity detection turns it into a link
                _output.Append(autolink.Url);
                break;
            case LineBreakInline:
                _output.Append(InSingleLine ? " " : HardLineBreak);
                break;
            case HtmlInline html:
                // Tags are shown verbatim: LLMs writing HTML usually mean it to be read, not rendered
                _output.Append(EscapeText(html.Tag));
                break;
            case HtmlEntityInline entity:
                _output.Append(EscapeText(entity.Transcoded.ToString()));
                break;
            case FootnoteLink { IsBackLink: false } footnoteLink:
                _output.Append("[^").Append(GetFootnoteLabel(footnoteLink.Footnote)).Append(']');
                break;
            case TaskList taskList:
                _output.Append(taskList.Checked ? "[x]" : "[ ]");
                break;
            case SpoilerInline spoiler:
                _output.Append("||").Append(EscapeText(spoiler.Content.ToString())).Append("||");
                break;
            case ContainerInline container:
                RenderInlines(container);
                break;
            default:
                break;
        }
    }

    private void RenderEmphasis(EmphasisInline emphasis)
    {
        var marker = (emphasis.DelimiterChar, emphasis.DelimiterCount) switch
        {
            ('*' or '_', >= 2) => "**",
            ('*', 1) => "*",
            ('_', 1) => "_",
            ('~', _) => "~~",
            ('=', _) => "==",
            _ => string.Empty,
        };

        if (marker.Length == 0)
        {
            // Unknown emphasis type: keep the content, just drop the styling
            RenderInlines(emphasis);
            return;
        }

        // A nested same-style marker is dropped: re-emitting it would be redundant markup
        var isNested = !_activeMarkers.Add(marker);
        if (!isNested)
        {
            _output.Append(marker);
        }

        RenderInlines(emphasis);

        if (!isNested)
        {
            _output.Append(marker);
            _activeMarkers.Remove(marker);
        }
    }

    private void RenderLink(LinkInline link)
    {
        if (link.IsAutoLink && !link.IsImage)
        {
            // Left unescaped so Telegram's automatic entity detection turns it into a link
            _output.Append(link.Url);
            return;
        }

        var label = BuildInlinesText(link);
        if (link.IsImage && label.Length == 0)
        {
            label = "Image";
        }

        // Images are emitted as regular links: Rich Markdown media blocks would make Telegram
        // fetch the URL, and a broken LLM-produced URL would fail the whole message
        _output.Append('[').Append(label).Append("](").Append(EscapeLinkUrl(link.Url ?? string.Empty)).Append(')');
    }

    private void RenderCodeInline(CodeInline code)
    {
        var content = code.Content;
        if (InTableCell)
        {
            // GFM processes table cell pipes before inline code, so they are escaped even here
            content = content.Replace("|", "\\|", StringComparison.Ordinal);
        }

        var fence = new string('`', content.LongestRunOf('`') + 1);
        var needsPadding = content.Length == 0
            || content[0] == '`'
            || content[^1] == '`'
            || (content[0] == ' ' && content[^1] == ' ');
        var padding = needsPadding ? " " : string.Empty;
        _output.Append(fence).Append(padding).Append(content).Append(padding).Append(fence);
    }

    private TelegramRichMarkdownRenderer CreateChild(bool singleLine = false) => new()
    {
        InTableCell = InTableCell,
        InSingleLine = InSingleLine || singleLine,
    };

    private string BuildBlockText(Block block, bool singleLine = false)
    {
        var renderer = CreateChild(singleLine);
        renderer.RenderBlock(block);
        return renderer._output.ToString().TrimEnd('\n');
    }

    private string BuildInlinesText(ContainerInline? container, bool singleLine = false)
    {
        var renderer = CreateChild(singleLine);
        renderer.RenderInlines(container);
        return renderer._output.ToString();
    }

    private static string PrefixLines(string text, string firstLinePrefix, string continuationPrefix)
    {
        var lines = text.Split('\n');
        var builder = new StringBuilder();
        for (var i = 0; i < lines.Length; i++)
        {
            if (i > 0)
            {
                builder.Append('\n');
            }

            if (i == 0)
            {
                builder.Append(firstLinePrefix);
            }
            else if (lines[i].Length > 0)
            {
                builder.Append(continuationPrefix);
            }
            builder.Append(lines[i]);
        }
        return builder.ToString();
    }

    private static string FlattenToSingleLine(string text)
    {
        var lines = text
            .Split('\n')
            .Select(static line => line.TrimEnd())
            .Where(static line => line.Length > 0);
        return string.Join(' ', lines).Trim();
    }

    private static int GetOrderedStart(ListBlock list)
        => int.TryParse(list.OrderedStart, NumberStyles.None, CultureInfo.InvariantCulture, out var start) ? start : 1;

    private string EscapeText(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        var result = new StringBuilder(text.Length + 8);
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (NeedsEscaping(text, i))
            {
                result.Append('\\');
            }
            result.Append(c);
        }
        return result.ToString();
    }

    private bool NeedsEscaping(string text, int index)
    {
        return text[index] switch
        {
            // These can start formatting on their own
            '\\' or '`' or '*' or '_' or '[' or '<' or '$' => true,

            // These are only meaningful when doubled (~~strike~~, ==marked==, ||spoiler||)
            '~' or '=' => IsDoubled(text, index),
            '|' => InTableCell || IsDoubled(text, index),
            _ => false,
        };
    }

    private bool IsDoubled(string text, int index)
    {
        var c = text[index];

        // The same character may arrive in the preceding inline node (e.g. "||" split in two literals),
        // so the already emitted output is checked as well
        return (index > 0 ? text[index - 1] == c : _output.Length > 0 && _output[^1] == c)
            || (index + 1 < text.Length && text[index + 1] == c);
    }

    private static string EscapeLinkUrl(string url)
    {
        if (string.IsNullOrEmpty(url))
        {
            return string.Empty;
        }

        return url
            .Replace("\\", @"\\", StringComparison.Ordinal)
            .Replace("(", "\\(", StringComparison.Ordinal)
            .Replace(")", "\\)", StringComparison.Ordinal)
            .Replace(" ", "%20", StringComparison.Ordinal);
    }
}
