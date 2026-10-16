using Pacos.Constants;
using Pacos.Extensions;

namespace Pacos.Models;

/// <summary>
/// The agent's reply to one turn. <see cref="TimedOut"/> marks a partial reply: the turn hit
/// its time budget, and only what was produced before the deadline is delivered.
/// </summary>
public sealed record ChatResponseInfo(string Text, IReadOnlyCollection<OutputFile> Files, bool TimedOut = false)
{
    private const string NoticeSeparator = "\n\n";

    /// <summary>
    /// The text to send, cut to <paramref name="maxLength"/>. A partial reply ends with
    /// <see cref="Const.TurnTimeoutNotice"/>; the text is cut before the notice is appended,
    /// so a long reply never loses it.
    /// </summary>
    public string ToReplyText(int maxLength)
    {
        if (!TimedOut)
        {
            return Text.Cut(maxLength);
        }

        if (string.IsNullOrWhiteSpace(Text))
        {
            return Const.TurnTimeoutNotice.Cut(maxLength);
        }

        var textLength = maxLength - NoticeSeparator.Length - Const.TurnTimeoutNotice.Length;
        return textLength <= 0
            ? Const.TurnTimeoutNotice.Cut(maxLength)
            : Text.Cut(textLength) + NoticeSeparator + Const.TurnTimeoutNotice;
    }
}
