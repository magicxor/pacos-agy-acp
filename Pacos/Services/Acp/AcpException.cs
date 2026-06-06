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
}
