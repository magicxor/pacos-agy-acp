using System.Text.Json;
using Telegram.Bot;
using Telegram.Bot.Types;

namespace Pacos.Services;

/// <summary>
/// Accepts Telegram webhook requests: verifies the secret token, parses the update, drops
/// repeated deliveries and hands the rest to the background queue, answering at once so
/// Telegram never retries a slow delivery.
/// </summary>
public sealed class TelegramWebhookHandler
{
    public const string SecretTokenHeader = "X-Telegram-Bot-Api-Secret-Token";

    private readonly ILogger<TelegramWebhookHandler> _logger;
    private readonly TelegramWebhookSecret _webhookSecret;
    private readonly ITelegramUpdateDispatcher _updateDispatcher;
    private readonly TelegramUpdateDeduplicator _updateDeduplicator;

    public TelegramWebhookHandler(
        ILogger<TelegramWebhookHandler> logger,
        TelegramWebhookSecret webhookSecret,
        ITelegramUpdateDispatcher updateDispatcher,
        TelegramUpdateDeduplicator updateDeduplicator)
    {
        _logger = logger;
        _webhookSecret = webhookSecret;
        _updateDispatcher = updateDispatcher;
        _updateDeduplicator = updateDeduplicator;
    }

    public async Task<IResult> HandleAsync(HttpRequest request, CancellationToken cancellationToken)
    {
        // Checked before the body is read, so a forged request costs no deserialization.
        if (!_webhookSecret.Matches(request.Headers[SecretTokenHeader]))
        {
            _logger.LogWarning("Rejected a Telegram webhook request with a missing or invalid secret token");
            return TypedResults.Unauthorized();
        }

        Update? update;
        try
        {
            update = await JsonSerializer.DeserializeAsync<Update>(request.Body, JsonBotAPI.Options, cancellationToken);
        }
        catch (JsonException e)
        {
            _logger.LogWarning(e, "Rejected a Telegram webhook request with a malformed update");
            return TypedResults.BadRequest();
        }

        if (update is null)
        {
            return TypedResults.BadRequest();
        }

        if (!_updateDeduplicator.TryReserve(update.Id))
        {
            _logger.LogInformation("Ignored a repeated Telegram webhook delivery of update {UpdateId}", update.Id);
            return TypedResults.Ok();
        }

        try
        {
            await _updateDispatcher.EnqueueUpdateAsync(update, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            // Telegram gave up on this delivery before the update was queued; let its repeat through.
            _updateDeduplicator.Release(update.Id);
            throw;
        }

        return TypedResults.Ok();
    }
}
