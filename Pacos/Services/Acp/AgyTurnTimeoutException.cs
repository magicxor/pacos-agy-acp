namespace Pacos.Services.Acp;

/// <summary>
/// Raised when an agent turn exhausts its time budget (agy's own <c>--print-timeout</c>
/// fires before the Pacos watchdog) without producing a usable reply — surfaced either as an
/// adapter error or as empty output. Deliberately not an <see cref="AcpException"/>: the chat
/// retry policy retries transient ACP failures but must not retry an over-budget turn, which
/// would only redo the same work and exhaust the budget again.
/// </summary>
public sealed class AgyTurnTimeoutException : Exception
{
    public AgyTurnTimeoutException(string message)
        : base(message)
    {
    }

    public AgyTurnTimeoutException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
