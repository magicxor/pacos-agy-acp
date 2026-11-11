using Telegram.Bot.Types;

namespace Pacos.Extensions;

public static class MessageQuotedFragment
{
    /// <summary>
    /// Returns the part of the replied-to message that this message quotes, or <c>null</c> when it
    /// quotes nothing. A blank quote carries no information, so it counts as no quote at all.
    /// A quote chosen from a rich message may leave <see cref="TextQuote.Text"/> empty and carry
    /// the quoted part in <see cref="TextQuote.RichTexts"/> instead.
    /// </summary>
    public static string? GetQuotedFragment(this Message message)
    {
        var quote = message.Quote;
        if (quote is null)
        {
            return null;
        }

        var quotedFragment = string.IsNullOrWhiteSpace(quote.Text)
            ? quote.RichTexts.GetPlainText()
            : quote.Text;

        return string.IsNullOrWhiteSpace(quotedFragment) ? null : quotedFragment.Trim();
    }
}
