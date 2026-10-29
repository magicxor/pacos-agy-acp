using Polly;

namespace Pacos.Services;

/// <summary>
/// Per-attempt deadlines for Telegram Bot API calls. A file upload gets a longer budget than
/// a plain API call: Telegram may answer slowly after receiving the file, and an attempt that
/// times out is retried, which posts the same message twice if Telegram did accept the first one.
/// </summary>
public static class TelegramRequestTimeouts
{
    public static readonly TimeSpan DefaultAttempt = TimeSpan.FromSeconds(40);

    public static readonly TimeSpan UploadAttempt = TimeSpan.FromSeconds(80);

    // Telegram.Bot encodes a request as multipart/form-data only when it carries a file stream.
    public static TimeSpan GetAttemptTimeout(ResilienceContext context)
        => context.GetRequestMessage()?.Content is MultipartFormDataContent ? UploadAttempt : DefaultAttempt;
}
