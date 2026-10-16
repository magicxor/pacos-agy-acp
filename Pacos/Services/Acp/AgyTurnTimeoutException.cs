namespace Pacos.Services.Acp;

/// <summary>
/// Raised when an agent turn exhausts its time budget without producing a usable reply:
/// agy-acp ended it at its deadline (<c>stopReason: "timeout"</c>) with neither text nor
/// output files, or agy-acp stopped responding until the Pacos watchdog fired. Deliberately
/// neither an <see cref="AcpException"/> nor a <see cref="TimeoutException"/>: the chat retry
/// policy retries transient ACP failures and session-setup timeouts, but must not retry an
/// over-budget turn, which would only redo the same work and exhaust the budget again.
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
