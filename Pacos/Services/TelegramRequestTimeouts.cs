using Polly;

namespace Pacos.Services;

/// <summary>
/// Per-attempt and total deadlines for Telegram Bot API calls. An attempt that times out is retried,
/// which posts the same message twice if Telegram did accept the first attempt, so only read-only
/// (get*) calls get short attempts and room for two retries; other calls get a single retry. A file
/// upload also gets a longer attempt, as Telegram may answer slowly after receiving the file.
/// </summary>
public static class TelegramRequestTimeouts
{
    public static readonly TimeSpan ReadAttempt = TimeSpan.FromSeconds(30);

    // Three attempts' worth; the retry delays between them cut the third one a little short.
    public static readonly TimeSpan ReadTotal = ReadAttempt * 3;

    public static readonly TimeSpan DefaultAttempt = TimeSpan.FromSeconds(40);

    public static readonly TimeSpan DefaultTotal = DefaultAttempt * 2;

    public static readonly TimeSpan UploadAttempt = TimeSpan.FromSeconds(80);

    public static readonly TimeSpan UploadTotal = UploadAttempt * 2;

    // Telegram holds a getUpdates call open for up to this long while there is nothing new, so it
    // must end well within the attempt timeout; otherwise every idle poll would time out and be retried.
    public static readonly TimeSpan LongPolling = ReadAttempt - TimeSpan.FromSeconds(15);

    public static TimeSpan GetAttemptTimeout(ResilienceContext context)
        => GetTimeouts(context.GetRequestMessage()).Attempt;

    public static TimeSpan GetTotalTimeout(ResilienceContext context)
        => GetTimeouts(context.GetRequestMessage()).Total;

    // Telegram.Bot encodes a request as multipart/form-data only when it carries a file stream,
    // and posts every call to .../bot<token>/<method>.
    private static (TimeSpan Attempt, TimeSpan Total) GetTimeouts(HttpRequestMessage? request) => request switch
    {
        { Content: MultipartFormDataContent } => (UploadAttempt, UploadTotal),
        { RequestUri: { IsAbsoluteUri: true } uri } when uri.Segments[^1].StartsWith("get", StringComparison.OrdinalIgnoreCase) => (ReadAttempt, ReadTotal),
        _ => (DefaultAttempt, DefaultTotal),
    };
}
