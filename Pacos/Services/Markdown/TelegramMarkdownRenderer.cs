using System.Collections.Frozen;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Markdig.Extensions.Alerts;
using Markdig.Extensions.Tables;
using Markdig.Extensions.TaskLists;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using Pacos.Services.Markdown.Spoiler;

namespace Pacos.Services.Markdown;

public sealed class TelegramMarkdownRenderer
{
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromSeconds(1);
    private static readonly FrozenSet<char> SpecialChars = new HashSet<char> { '_', '*', '[', ']', '(', ')', '~', '`', '>', '#', '+', '-', '=', '|', '{', '}', '.', '!' }.ToFrozenSet();

    private readonly StringBuilder _output = new();
    private readonly HashSet<string> _activeMarkers = new(StringComparer.Ordinal);

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
                _output.AppendLine("\\-\\-\\-");
                _output.AppendLine();
                break;
            case HtmlBlock html:
                RenderHtmlBlock(html);
                break;
            default:
                // For other block types, try to extract and render any inline content
                if (block is ContainerBlock container)
                {
                    foreach (var child in container)
                    {
                        RenderBlock(child);
                    }
                }
                break;
        }
    }

    private void RenderHeading(HeadingBlock heading)
    {
        // Telegram doesn't support headers, so we'll make them bold
        _activeMarkers.Add("*");
        _output.Append('*');
        if (heading.Inline != null)
        {
            foreach (var inline in heading.Inline)
            {
                RenderInline(inline);
            }
        }
        _output.AppendLine("*");
        _activeMarkers.Remove("*");
        _output.AppendLine();
    }

    private void RenderParagraph(ParagraphBlock paragraph)
    {
        if (paragraph.Inline != null)
        {
            foreach (var inline in paragraph.Inline)
            {
                RenderInline(inline);
            }
        }
        _output.AppendLine();
        _output.AppendLine();
    }

    private void RenderList(ListBlock list)
    {
        RenderListItems(list, string.Empty, _output);
        EnsureTrailingLineBreaks(_output, 2);
    }

    private string RenderListDirectly(ListBlock list, string indent)
    {
        var nestedOutput = new StringBuilder();
        RenderListItems(list, indent, nestedOutput);
        return nestedOutput.ToString();
    }

    private void RenderListItems(ListBlock list, string indent, StringBuilder output)
    {
        int index = GetOrderedStart(list);
        foreach (var listItemBlock in list)
        {
            var item = (ListItemBlock)listItemBlock;
            bool isTaskList = TryGetTaskListCheckbox(list, item, out string checkboxText, out var taskListContentStart);

            if (list.IsOrdered)
            {
                output.Append(CultureInfo.InvariantCulture, $"{indent}{index}\\. ");
                index++;
            }
            else if (isTaskList)
            {
                output.Append(CultureInfo.InvariantCulture, $"{indent}\\- {checkboxText}");
            }
            else
            {
                output.Append(CultureInfo.InvariantCulture, $"{indent}• ");
            }

            if (isTaskList)
            {
                // Render all inline elements after the checkbox; the source text after "[x]" starts
                // with its own space, so the first piece is trimmed to keep the single checkbox space
                var current = taskListContentStart;
                bool isFirstInline = true;
                while (current != null)
                {
                    string inlineText = RenderInlineToString(current);
                    output.Append(isFirstInline ? inlineText.TrimStart(' ') : inlineText);
                    isFirstInline = false;
                    current = current.NextSibling;
                }
            }
            else
            {
                RenderListItemBlocks(list, item, indent, output);
            }
            EnsureTrailingLineBreaks(output, list.IsLoose ? 2 : 1);
        }
    }

    private void RenderListItemBlocks(ListBlock list, ListItemBlock item, string indent, StringBuilder output)
    {
        bool isFirstBlock = true;
        bool previousBlockWasParagraph = false;
        bool previousBlockWasNestedList = false;
        foreach (var block in item)
        {
            if (block is ParagraphBlock para)
            {
                if (!isFirstBlock)
                {
                    output.AppendLine();
                    if (previousBlockWasParagraph || (previousBlockWasNestedList && list.IsLoose))
                    {
                        output.AppendLine();
                    }
                }
                if (para.Inline != null)
                {
                    foreach (var inline in para.Inline)
                    {
                        output.Append(RenderInlineToString(inline));
                    }
                }
                previousBlockWasParagraph = true;
                previousBlockWasNestedList = false;
            }
            else if (block is ListBlock nestedList)
            {
                // Add a line break before nested lists but no extra line;
                // trim the trailing newline from nested content to avoid double spacing
                if (!isFirstBlock)
                {
                    output.AppendLine();
                }
                output.Append(RenderListDirectly(nestedList, indent + "  ").TrimEnd());
                previousBlockWasParagraph = false;
                previousBlockWasNestedList = true;
            }
            else
            {
                // Add line break before non-paragraph blocks (like quotes) if this is not the first block
                if (!isFirstBlock)
                {
                    output.AppendLine();
                    if (previousBlockWasParagraph && block is CodeBlock && list.IsLoose)
                    {
                        output.AppendLine();
                    }
                }

                // Remove trailing blank line added by block renderers (e.g. RenderQuote)
                // to avoid double spacing — the list item loop adds its own newline.
                // When buffering (nested lists), all trailing whitespace is trimmed instead.
                if (ReferenceEquals(output, _output))
                {
                    RenderBlock(block);
                    TrimTrailingBlankLine();
                }
                else
                {
                    var blockRenderer = new TelegramMarkdownRenderer();
                    blockRenderer.RenderBlock(block);
                    blockRenderer.TrimTrailingBlankLine();
                    output.Append(blockRenderer._output.ToString().TrimEnd());
                }
                previousBlockWasParagraph = false;
                previousBlockWasNestedList = false;
            }
            isFirstBlock = false;
        }
    }

    private static bool TryGetTaskListCheckbox(ListBlock list, ListItemBlock item, out string checkboxText, out Inline? contentStart)
    {
        checkboxText = string.Empty;
        contentStart = null;

        if (list.IsOrdered || item.Count == 0 || item[0] is not ParagraphBlock firstPara || firstPara.Inline == null)
        {
            return false;
        }

        if (firstPara.Inline.FirstChild is not TaskList taskList)
        {
            return false;
        }

        checkboxText = taskList.Checked ? @"\[x\] " : @"\[ \] ";
        contentStart = taskList.NextSibling;
        return true;
    }

    private static string RenderInlineToString(Inline inline)
    {
        var renderer = new TelegramMarkdownRenderer();
        renderer.RenderInline(inline);
        return renderer._output.ToString();
    }

    private void RenderAlert(AlertBlock alert)
    {
        string kind = alert.Kind.ToString();
        (string emoji, string label) = kind.ToUpperInvariant() switch
        {
            "NOTE" => ("ℹ️", "Note"),
            "TIP" => ("💡", "Tip"),
            "IMPORTANT" => ("❗", "Important"),
            "WARNING" => ("⚠️", "Warning"),
            "CAUTION" => ("🛑", "Caution"),
            _ => (string.Empty, kind),
        };

        string labelLine = string.IsNullOrEmpty(emoji) ? label : $"{emoji} {label}";
        if (labelLine.Length > 0)
        {
            _output.AppendLine(CultureInfo.InvariantCulture, $">*{EscapeText(labelLine)}*");
        }
        RenderQuote(alert);
    }

    private void RenderQuote(QuoteBlock quote)
    {
        // TODO: RemoveEmptyEntries below drops blank lines, so a multi-paragraph quote is glued
        // into consecutive lines and the paragraph separation is lost. The proper fix is to emit
        // a bare ">" separator line between paragraphs (that is how MarkdownV2 expresses a blank
        // line inside a single quote) and to keep interior blank lines of nested blocks with the
        // ">" prefix. Before fixing, verify with a live message that Bot API accepts a quote
        // containing a bare ">" line and does not reject it or split the quote entity.
        foreach (var block in quote)
        {
            if (block is ParagraphBlock para)
            {
                // Collect all the text from this paragraph first
                var paraRenderer = new TelegramMarkdownRenderer();
                if (para.Inline != null)
                {
                    foreach (var inline in para.Inline)
                    {
                        paraRenderer.RenderInline(inline);
                    }
                }

                // Split the content by lines and add > prefix to each
                string paraContent = paraRenderer._output.ToString().Trim();
                string[] lines = paraContent.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
                foreach (string line in lines)
                {
                    _output.AppendLine(">" + line);
                }
            }
            else
            {
                // For non-paragraph blocks, render with quote prefix
                var blockRenderer = new TelegramMarkdownRenderer();
                blockRenderer.RenderBlock(block);
                string blockContent = blockRenderer._output.ToString();
                string[] lines = blockContent.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
                foreach (string line in lines)
                {
                    _output.AppendLine(">" + line);
                }
            }
        }
        _output.AppendLine();
    }

    private void RenderCodeBlock(CodeBlock code)
    {
        if (code is FencedCodeBlock fenced && !string.IsNullOrEmpty(fenced.Info))
        {
            _output.AppendLine(CultureInfo.InvariantCulture, $"```{EscapeCodeContent(fenced.Info)}");
        }
        else
        {
            _output.AppendLine("```");
        }

        foreach (var line in code.Lines)
        {
            _output.AppendLine(EscapeCodeContent(line.ToString() ?? string.Empty));
        }
        _output.AppendLine("```");
        _output.AppendLine();
    }

    private void RenderTable(Table table)
    {
        // Telegram doesn't support tables, so we'll render as preformatted text
        _output.AppendLine("```");

        foreach (var row in table)
        {
            if (row is TableRow tableRow)
            {
                var cells = new List<string>();
                foreach (var cell in tableRow)
                {
                    if (cell is TableCell tableCell)
                    {
                        cells.Add(GetPlainText(tableCell));
                    }
                }
                _output.AppendLine(EscapeCodeContent(string.Join(" | ", cells)));
            }
        }

        _output.AppendLine("```");
        _output.AppendLine();
    }

    private void RenderHtmlBlock(HtmlBlock html)
    {
        // Convert simple HTML tags to Telegram markdown
        string content = html.Lines.ToString() ?? string.Empty;
        content = Regex.Replace(content, "<b>(.*?)</b>", "*$1*", RegexOptions.IgnoreCase, RegexTimeout);
        content = Regex.Replace(content, "<i>(.*?)</i>", "_$1_", RegexOptions.IgnoreCase, RegexTimeout);
        content = Regex.Replace(content, "<u>(.*?)</u>", "__$1__", RegexOptions.IgnoreCase, RegexTimeout);
        content = Regex.Replace(content, "<s>(.*?)</s>", "~$1~", RegexOptions.IgnoreCase, RegexTimeout);
        content = Regex.Replace(content, "<code>(.*?)</code>", "`$1`", RegexOptions.IgnoreCase, RegexTimeout);
        content = Regex.Replace(content, "<[^>]+>", string.Empty, RegexOptions.IgnoreCase, RegexTimeout); // Remove other HTML tags

        _output.AppendLine(EscapeText(content));
        _output.AppendLine();
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
                _output.Append(CultureInfo.InvariantCulture, $"`{EscapeCodeContent(code.Content)}`");
                break;
            case LineBreakInline:
                _output.AppendLine();
                break;
            case HtmlInline html:
                // Tags are shown verbatim: LLMs writing HTML usually mean it to be read, not rendered
                _output.Append(EscapeText(html.Tag));
                break;
            case HtmlEntityInline entity:
                _output.Append(EscapeText(entity.Transcoded.ToString()));
                break;
            case AutolinkInline autolink:
                _output.Append(CultureInfo.InvariantCulture, $"[{EscapeText(autolink.Url)}]({EscapeLinkUrl(autolink.Url)})");
                break;
            case SpoilerInline spoiler:
                _output.Append("||");
                _output.Append(EscapeText(spoiler.Content.ToString()));
                _output.Append("||");
                break;
            default:
                // For unknown inline types, check if it's a container
                if (inline is ContainerInline container)
                {
                    foreach (var child in container)
                    {
                        RenderInline(child);
                    }
                }
                break;
        }
    }

    private void RenderEmphasis(EmphasisInline emphasis)
    {
        string marker = string.Empty;

        // Handle different emphasis types based on delimiter character and count
        if (emphasis.DelimiterChar == '_' && emphasis.DelimiterCount == 2)
        {
            // Double underscore should be underline in Telegram
            marker = "__";
        }
        else if (emphasis.DelimiterChar == '*' && emphasis.DelimiterCount == 2)
        {
            // Double asterisk is bold
            marker = "*";
        }
        else if (emphasis.DelimiterChar == '_' && emphasis.DelimiterCount == 1)
        {
            // Single underscore is italic
            // zero-width space is used to avoid conflicts
            marker = "\u200B_\u200B";
        }
        else if (emphasis.DelimiterChar == '*' && emphasis.DelimiterCount == 1)
        {
            // Single asterisk is italic (but we prefer underscore for consistency)
            // zero-width space is used to avoid conflicts
            marker = "\u200B_\u200B";
        }
        else if (emphasis.DelimiterChar == '~')
        {
            // Strikethrough
            marker = "~";
        }

        if (string.IsNullOrEmpty(marker))
        {
            // Unknown emphasis type: keep the content, just drop the styling
            foreach (var child in emphasis)
            {
                RenderInline(child);
            }
            return;
        }

        // Telegram pairs identical markers greedily, so a nested same-style marker would invert the formatting
        bool isNested = !_activeMarkers.Add(marker);
        if (!isNested)
        {
            _output.Append(marker);
        }

        foreach (var child in emphasis)
        {
            RenderInline(child);
        }

        if (!isNested)
        {
            _output.Append(marker);
            _activeMarkers.Remove(marker);
        }
    }

    private void RenderLink(LinkInline link)
    {
        if (link.IsImage)
        {
            // Images are not supported in Telegram markdown, show as a link instead
            _output.Append('[');
            // Use alt text from the image, or "Image" as fallback
            foreach (var child in link)
            {
                RenderInline(child);
            }
            // If no alt text was found, use a default
            if (link.FirstChild == null)
            {
                _output.Append("Image");
            }
            _output.Append(CultureInfo.InvariantCulture, $"]({EscapeLinkUrl(link.Url ?? string.Empty)})");
        }
        else
        {
            _output.Append('[');
            foreach (var child in link)
            {
                RenderInline(child);
            }
            _output.Append(CultureInfo.InvariantCulture, $"]({EscapeLinkUrl(link.Url ?? string.Empty)})");
        }
    }

    private void TrimTrailingBlankLine()
    {
        // Normalize multiple trailing blank lines with '\n' endings to a single trailing '\n'
        while (_output.Length >= 2 && _output[^1] == '\n' && _output[^2] == '\n')
        {
            _output.Length--;
        }

        // Also normalize multiple trailing blank lines with '\r\n' endings to a single trailing '\r\n'
        while (_output.Length >= 4 && _output[^1] == '\n' && _output[^2] == '\r' && _output[^3] == '\n' && _output[^4] == '\r')
        {
            _output.Length -= 2;
        }
    }

    private static void EnsureTrailingLineBreaks(StringBuilder output, int lineBreakCount)
    {
        int trailingLineBreaks = 0;
        int i = output.Length - 1;

        while (i >= 0)
        {
            if (output[i] == '\n')
            {
                trailingLineBreaks++;
                i--;
                if (i >= 0 && output[i] == '\r')
                {
                    i--;
                }
                continue;
            }

            if (output[i] == '\r')
            {
                i--;
                continue;
            }

            break;
        }

        if (trailingLineBreaks > 0)
        {
            output.Length = i + 1;
        }

        for (int j = 0; j < lineBreakCount; j++)
        {
            output.AppendLine();
        }
    }

    private static int GetOrderedStart(ListBlock list)
        => int.TryParse(list.OrderedStart, NumberStyles.None, CultureInfo.InvariantCulture, out int start) ? start : 1;

    private static string EscapeText(string text)
    {
        if (string.IsNullOrEmpty(text)) return string.Empty;

        var result = new StringBuilder();
        foreach (char c in text)
        {
            if (SpecialChars.Contains(c) || c == '\\')
            {
                result.Append('\\');
            }
            result.Append(c);
        }
        return result.ToString();
    }

    private static string EscapeCodeContent(string text)
    {
        if (string.IsNullOrEmpty(text)) return string.Empty;

        return text.Replace("\\", @"\\", StringComparison.Ordinal).Replace("`", "\\`", StringComparison.Ordinal);
    }

    private static string EscapeLinkUrl(string url)
    {
        if (string.IsNullOrEmpty(url)) return string.Empty;

        return url.Replace("\\", @"\\", StringComparison.Ordinal).Replace(")", "\\)", StringComparison.Ordinal);
    }

    private string GetPlainText(Block block)
    {
        switch (block)
        {
            case ListBlock list:
                return string.Join(", ", list.OfType<ListItemBlock>().Select(GetPlainText).Where(static text => text.Length > 0));
            case LeafBlock { Inline: not null } leaf:
                var result = new StringBuilder();
                foreach (var inline in leaf.Inline)
                {
                    result.Append(GetPlainText(inline));
                }
                return result.ToString();
            case LeafBlock leaf:
                return (leaf.Lines.ToString() ?? string.Empty).ReplaceLineEndings(" ");
            case ContainerBlock container:
                return string.Join(" ", container.Select(GetPlainText).Where(static text => text.Length > 0));
            default:
                return string.Empty;
        }
    }

    private string GetPlainText(Inline inline)
    {
        switch (inline)
        {
            case LiteralInline literal:
                return literal.Content.ToString();
            case EmphasisInline emphasis:
                var result = new StringBuilder();
                foreach (var child in emphasis)
                {
                    result.Append(GetPlainText(child));
                }
                return result.ToString();
            case LinkInline link:
                var linkResult = new StringBuilder();
                foreach (var child in link)
                {
                    linkResult.Append(GetPlainText(child));
                }
                return linkResult.ToString();
            case CodeInline code:
                return code.Content;
            case HtmlEntityInline entity:
                return entity.Transcoded.ToString();
            case AutolinkInline autolink:
                return autolink.Url;
            case HtmlInline html:
                return html.Tag;
            case SpoilerInline spoiler:
                return spoiler.Content.ToString();
            default:
                return string.Empty;
        }
    }
}
