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
            case RichBlockParagraph b:        AppendText(sb, b.Text); sb.Append('\n'); break;
            case RichBlockSectionHeading b:   AppendText(sb, b.Text); sb.Append('\n'); break;
            case RichBlockFooter b:           AppendText(sb, b.Text); sb.Append('\n'); break;
            case RichBlockPreformatted b:     AppendText(sb, b.Text); sb.Append('\n'); break;

            // container blocks (contain nested blocks)
            case RichBlockBlockQuotation b:   AppendBlocks(sb, b.Blocks); break;
            case RichBlockList b:
                foreach (var item in b.Items)
                    AppendBlocks(sb, item.Blocks);
                break;
            case RichBlockTable b:
                foreach (var row in b.Cells)
                    foreach (var cell in row)
                    {
                        AppendText(sb, cell.Text);
                        sb.Append('\t');
                    }
                sb.Append('\n');
                break;

            // RichBlockDivider, RichBlockAnchor, RichBlockMap, media blocks, etc. — no text
            default: break;
        }
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
            case RichTextBold t:          AppendText(sb, t.Text); break;
            case RichTextItalic t:        AppendText(sb, t.Text); break;
            case RichTextUnderline t:     AppendText(sb, t.Text); break;
            case RichTextStrikethrough t: AppendText(sb, t.Text); break;
            case RichTextSpoiler t:       AppendText(sb, t.Text); break;
            case RichTextCode t:          AppendText(sb, t.Text); break;
            case RichTextUrl t:           AppendText(sb, t.Text); break;
            case RichTextEmailAddress t:  AppendText(sb, t.Text); break;
            case RichTextPhoneNumber t:   AppendText(sb, t.Text); break;
            case RichTextTextMention t:   AppendText(sb, t.Text); break;
            case RichTextMention t:       AppendText(sb, t.Text); break;
            case RichTextHashtag t:       AppendText(sb, t.Text); break;
            case RichTextCashtag t:       AppendText(sb, t.Text); break;
            case RichTextBotCommand t:    AppendText(sb, t.Text); break;
            case RichTextMarked t:        AppendText(sb, t.Text); break;
            case RichTextSubscript t:     AppendText(sb, t.Text); break;
            case RichTextSuperscript t:   AppendText(sb, t.Text); break;
            case RichTextDateTime t:      AppendText(sb, t.Text); break;

            // these have no nested ".Text" — use a meaningful value
            case RichTextCustomEmoji t:   sb.Append(t.AlternativeText); break;
            case RichTextMathematicalExpression t: sb.Append(t.Expression); break;

            // RichTextAnchor (name only), RichTextReference*, etc. — at your discretion
            default: break;
        }
    }
}
