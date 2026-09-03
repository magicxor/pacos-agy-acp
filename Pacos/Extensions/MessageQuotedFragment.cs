using Telegram.Bot.Types;

namespace Pacos.Extensions;

public static class MessageQuotedFragment
{
    /// <summary>
    /// Returns the part of the replied-to message that this message quotes, or <c>null</c> when it
    /// quotes nothing. A blank quote carries no information, so it counts as no quote at all.
    /// </summary>
    public static string? GetQuotedFragment(this Message message)
    {
        var quotedFragment = message.Quote?.Text;
        return string.IsNullOrWhiteSpace(quotedFragment) ? null : quotedFragment.Trim();
    }
}
