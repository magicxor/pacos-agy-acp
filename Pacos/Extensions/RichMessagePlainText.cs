using System.Text;
using Telegram.Bot.Types;

namespace Pacos.Extensions;

#pragma warning disable SA1025,S1871

public static class RichMessagePlainText
{
    public static string GetPlainText(this RichMessage? msg)
    {
        if (msg is null) return string.Empty;
        var sb = new StringBuilder();
        AppendBlocks(sb, msg.Blocks);
        return sb.ToString().TrimEnd();
    }

    private static void AppendBlocks(StringBuilder sb, RichBlock[]? blocks)
    {
        if (blocks is null) return;
        foreach (var block in blocks)
            AppendBlock(sb, block);
    }

    private static void AppendBlock(StringBuilder sb, RichBlock block)
    {
        switch (block)
        {
            // blocks with a single RichText
            case RichBlockParagraph b:      AppendTextLine(sb, b.Text); break;
            case RichBlockSectionHeading b: AppendTextLine(sb, b.Text); break;
            case RichBlockFooter b:         AppendTextLine(sb, b.Text); break;
            case RichBlockPreformatted b:   AppendTextLine(sb, b.Text); break;
            case RichBlockThinking b:       AppendTextLine(sb, b.Text); break;
            case RichBlockMathematicalExpression b: AppendStringLine(sb, b.Expression); break;

            case RichBlockPullQuotation b:
                AppendTextLine(sb, b.Text);
                AppendTextLine(sb, b.Credit);
                break;

            // container blocks (contain nested blocks)
            case RichBlockBlockQuotation b:
                AppendBlocks(sb, b.Blocks);
                AppendTextLine(sb, b.Credit);
                break;
            case RichBlockDetails b:
                AppendTextLine(sb, b.Summary);
                AppendBlocks(sb, b.Blocks);
                break;
            case RichBlockList b:
                foreach (var item in b.Items)
                    AppendListItem(sb, item);
                break;
            case RichBlockTable b:
                AppendTextLine(sb, b.Caption);
                AppendTableRows(sb, b.Cells);
                break;

            // media containers: no own text, but nested blocks and the caption may have some
            case RichBlockCollage b:   AppendBlocks(sb, b.Blocks); AppendCaption(sb, b.Caption); break;
            case RichBlockSlideshow b: AppendBlocks(sb, b.Blocks); AppendCaption(sb, b.Caption); break;

            // media blocks: the caption is the only text
            case RichBlockPhoto b:     AppendCaption(sb, b.Caption); break;
            case RichBlockVideo b:     AppendCaption(sb, b.Caption); break;
            case RichBlockAnimation b: AppendCaption(sb, b.Caption); break;
            case RichBlockAudio b:     AppendCaption(sb, b.Caption); break;
            case RichBlockVoiceNote b: AppendCaption(sb, b.Caption); break;
            case RichBlockMap b:       AppendCaption(sb, b.Caption); break;

            // RichBlockDivider, RichBlockAnchor — no text
            default: break;
        }
    }

    private static void AppendListItem(StringBuilder sb, RichBlockListItem item)
    {
        if (item.HasCheckbox)
            sb.Append(item.IsChecked ? "[x] " : "[ ] ");

        if (!string.IsNullOrEmpty(item.Label))
        {
            sb.Append(item.Label);
            sb.Append(' ');
        }

        AppendBlocks(sb, item.Blocks);
    }

    // Tables are rendered in markdown pipe format so that downstream consumers (the LLM prompt)
    // can still recognize the tabular structure and the header row
    private static void AppendTableRows(StringBuilder sb, RichBlockTableCell[][] rows)
    {
        for (var i = 0; i < rows.Length; i++)
        {
            AppendTableRow(sb, rows[i]);

            if (i == 0 && Array.Exists(rows[i], static cell => cell.IsHeader))
                AppendTableDelimiterRow(sb, rows[i].Length);
        }
    }

    private static void AppendTableRow(StringBuilder sb, RichBlockTableCell[] row)
    {
        sb.Append('|');
        foreach (var cell in row)
            sb.Append(' ').Append(GetTableCellText(cell)).Append(" |");

        sb.Append('\n');
    }

    private static void AppendTableDelimiterRow(StringBuilder sb, int columnCount)
    {
        sb.Append('|');
        for (var i = 0; i < columnCount; i++)
            sb.Append(" --- |");

        sb.Append('\n');
    }

    // The link target is kept in markdown syntax; without it the URL would be lost for the LLM prompt
    private static void AppendUrl(StringBuilder sb, RichTextUrl url)
    {
        var textSb = new StringBuilder();
        AppendText(textSb, url.Text);
        var text = textSb.ToString();

        if (string.IsNullOrEmpty(url.Url) || text == url.Url)
        {
            sb.Append(text);
            return;
        }

        sb.Append('[').Append(text).Append("](").Append(url.Url).Append(')');
    }

    private static string GetTableCellText(RichBlockTableCell cell)
    {
        var sb = new StringBuilder();
        AppendText(sb, cell.Text);
        return sb.ToString()
            .ReplaceLineEndings(" ")
            .Replace("|", "\\|", StringComparison.Ordinal)
            .Trim();
    }

    private static void AppendCaption(StringBuilder sb, RichBlockCaption? caption)
    {
        if (caption is null) return;
        AppendTextLine(sb, caption.Text);
        AppendTextLine(sb, caption.Credit);
    }

    private static void AppendTextLine(StringBuilder sb, RichText? text)
    {
        if (text is null) return;
        AppendText(sb, text);
        sb.Append('\n');
    }

    private static void AppendStringLine(StringBuilder sb, string? text)
    {
        if (string.IsNullOrEmpty(text)) return;
        sb.Append(text);
        sb.Append('\n');
    }

    // recursively extract text from the RichText tree
    private static void AppendText(StringBuilder sb, RichText? text)
    {
        switch (text)
        {
            case null: break;
            case RichTextText t:  sb.Append(t.Text); break;
            case RichTextArray a:
                foreach (var child in a.Array)
                    AppendText(sb, child);
                break;

            // all formatting wrappers have a .Text of type RichText
            case RichTextBold t:           AppendText(sb, t.Text); break;
            case RichTextItalic t:         AppendText(sb, t.Text); break;
            case RichTextUnderline t:      AppendText(sb, t.Text); break;
            case RichTextStrikethrough t:  AppendText(sb, t.Text); break;
            case RichTextSpoiler t:        AppendText(sb, t.Text); break;
            case RichTextCode t:           AppendText(sb, t.Text); break;
            case RichTextUrl t:            AppendUrl(sb, t); break;
            case RichTextEmailAddress t:   AppendText(sb, t.Text); break;
            case RichTextPhoneNumber t:    AppendText(sb, t.Text); break;
            case RichTextBankCardNumber t: AppendText(sb, t.Text); break;
            case RichTextTextMention t:    AppendText(sb, t.Text); break;
            case RichTextMention t:        AppendText(sb, t.Text); break;
            case RichTextHashtag t:        AppendText(sb, t.Text); break;
            case RichTextCashtag t:        AppendText(sb, t.Text); break;
            case RichTextBotCommand t:     AppendText(sb, t.Text); break;
            case RichTextMarked t:         AppendText(sb, t.Text); break;
            case RichTextSubscript t:      AppendText(sb, t.Text); break;
            case RichTextSuperscript t:    AppendText(sb, t.Text); break;
            case RichTextDateTime t:       AppendText(sb, t.Text); break;
            case RichTextAnchorLink t:     AppendText(sb, t.Text); break;
            case RichTextReference t:      AppendText(sb, t.Text); break;
            case RichTextReferenceLink t:  AppendText(sb, t.Text); break;

            // these have no nested ".Text" — use a meaningful value
            case RichTextCustomEmoji t:            sb.Append(t.AlternativeText); break;
            case RichTextMathematicalExpression t: sb.Append(t.Expression); break;

            // RichTextAnchor — an invisible named anchor, no text
            default: break;
        }
    }
}
