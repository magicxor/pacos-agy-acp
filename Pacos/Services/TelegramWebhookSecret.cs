using System.Security.Cryptography;
using System.Text;

namespace Pacos.Services;

/// <summary>
/// Secret token registered with the Telegram webhook, generated once per process run.
/// Telegram echoes it in every webhook request, which is how forged updates are told apart:
/// re-registering the webhook on each start makes a persisted secret unnecessary.
/// </summary>
public sealed class TelegramWebhookSecret
{
    private const int SecretByteLength = 32;

    private readonly byte[] _valueBytes;

    public TelegramWebhookSecret()
    {
        // Hex keeps the token within Telegram's allowed alphabet (A-Z, a-z, 0-9, _ and -).
        Value = Convert.ToHexString(RandomNumberGenerator.GetBytes(SecretByteLength));
        _valueBytes = Encoding.UTF8.GetBytes(Value);
    }

    public string Value { get; }

    public bool Matches(string? candidate)
        => candidate is not null
           && CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(candidate), _valueBytes);
}
