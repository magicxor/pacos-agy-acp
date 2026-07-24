namespace Pacos.Services.Acp;

/// <summary>
/// Raised when the agy-acp adapter reports a JSON-RPC error, terminates
/// unexpectedly, or otherwise violates the expected protocol flow.
/// </summary>
public sealed class AcpException : Exception
{
    public AcpException(string message)
        : base(message)
    {
    }

    public AcpException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    /// <summary>
    /// True when the reported error looks like an upstream quota/rate-limit
    /// rejection (HTTP 429 / RESOURCE_EXHAUSTED), as surfaced by the agy-acp
    /// adapter from agy's stderr or its cli.log. Drives the decision to retry
    /// the prompt on a fallback model served from a separate quota pool.
    /// </summary>
    public bool IsQuotaError =>
        Message.Contains("RESOURCE_EXHAUSTED", StringComparison.Ordinal)
        || Message.Contains("code 429", StringComparison.OrdinalIgnoreCase)
        || Message.Contains("quota", StringComparison.OrdinalIgnoreCase);
}
