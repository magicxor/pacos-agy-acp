using Telegram.Bot;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

namespace Pacos.Services;

/// <summary>
/// Receives updates by long polling: Telegram holds each getUpdates call open for up to
/// <see cref="TelegramRequestTimeouts.LongPolling"/> while there is nothing new. Replaces the
/// library's StartReceiving, which takes the poll timeout from HttpClient.Timeout - infinite
/// here, since Polly owns the timeouts - and so degrades into back-to-back short polls.
/// </summary>
public sealed class TelegramLongPollingReceiver
{
    internal static readonly TimeSpan ErrorDelay = TimeSpan.FromSeconds(5);

    private const int UpdateLimit = 100;

    private static readonly int LongPollingSeconds = (int)TelegramRequestTimeouts.LongPolling.TotalSeconds;

    private readonly ILogger<TelegramLongPollingReceiver> _logger;
    private readonly ITelegramBotClient _botClient;
    private readonly TimeProvider _timeProvider;

    public TelegramLongPollingReceiver(
        ILogger<TelegramLongPollingReceiver> logger,
        ITelegramBotClient botClient,
        TimeProvider timeProvider)
    {
        _logger = logger;
        _botClient = botClient;
        _timeProvider = timeProvider;
    }

    /// <summary>
    /// Polls until <paramref name="cancellationToken"/> is cancelled (then throws
    /// <see cref="OperationCanceledException"/>), handing every update to
    /// <paramref name="dispatcher"/> in order. A failed poll is logged and retried after a pause.
    /// </summary>
    public async Task ReceiveAsync(
        ITelegramUpdateDispatcher dispatcher,
        IEnumerable<UpdateType> allowedUpdates,
        CancellationToken cancellationToken)
    {
        int? offset = null;
        while (!cancellationToken.IsCancellationRequested)
        {
            Update[] updates;
            try
            {
                updates = await _botClient.GetUpdates(offset, UpdateLimit, LongPollingSeconds, allowedUpdates, cancellationToken);
            }
            catch (RequestException e) when (cancellationToken.IsCancellationRequested)
            {
                // Telegram.Bot wraps some cancellations of an in-flight request (e.g. Polly's) as a failure.
                throw new OperationCanceledException("Telegram polling was cancelled.", e, cancellationToken);
            }
            catch (RequestException e)
            {
                LogPollingError(e);
                await Task.Delay(GetErrorDelay(e), _timeProvider, cancellationToken);
                continue;
            }

            foreach (var update in updates)
            {
                // The next poll's offset confirms to Telegram everything up to this update.
                offset = update.Id + 1;
                await dispatcher.EnqueueUpdateAsync(update, cancellationToken);
            }
        }
    }

    // Flood control (429) may ask for a longer pause than the default one.
    private static TimeSpan GetErrorDelay(RequestException exception)
        => exception is ApiRequestException { Parameters.RetryAfter: { } retryAfterSeconds }
           && TimeSpan.FromSeconds(retryAfterSeconds) > ErrorDelay
            ? TimeSpan.FromSeconds(retryAfterSeconds)
            : ErrorDelay;

    private void LogPollingError(RequestException exception)
    {
        if (exception is ApiRequestException apiRequestException)
        {
            _logger.LogError(exception,
                "Telegram API Error. ErrorCode={ErrorCode}, RetryAfter={RetryAfter}, MigrateToChatId={MigrateToChatId}",
                apiRequestException.ErrorCode,
                apiRequestException.Parameters?.RetryAfter,
                apiRequestException.Parameters?.MigrateToChatId);
            return;
        }

        _logger.LogError(exception, "Telegram API Error");
    }
}
