using Microsoft.Extensions.Options;
using Pacos.Constants;
using Pacos.Extensions;
using Pacos.Models.Options;
using Pacos.Services.BackgroundTasks;
using Pacos.Services.ChatCommandHandlers;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

namespace Pacos.Services;

public sealed class TelegramBotService : ITelegramUpdateDispatcher
{
    private readonly ILogger<TelegramBotService> _logger;
    private readonly IOptions<PacosOptions> _options;
    private readonly ITelegramBotClient _telegramBotClient;
    private readonly IBackgroundTaskQueue _taskQueue;
    private readonly DrawHandler _drawHandler;
    private readonly ResetHandler _resetHandler;
    private readonly MentionHandler _mentionHandler;
    private readonly TelegramWebhookSecret _webhookSecret;
    private readonly TelegramLongPollingReceiver _longPollingReceiver;

    private static readonly UpdateType[] AllowedUpdates = [UpdateType.Message];

    public TelegramBotService(
        ILogger<TelegramBotService> logger,
        IOptions<PacosOptions> options,
        ITelegramBotClient telegramBotClient,
        IBackgroundTaskQueue taskQueue,
        DrawHandler drawHandler,
        ResetHandler resetHandler,
        MentionHandler mentionHandler,
        TelegramWebhookSecret webhookSecret,
        TelegramLongPollingReceiver longPollingReceiver)
    {
        _logger = logger;
        _options = options;
        _telegramBotClient = telegramBotClient;
        _taskQueue = taskQueue;
        _drawHandler = drawHandler;
        _resetHandler = resetHandler;
        _mentionHandler = mentionHandler;
        _webhookSecret = webhookSecret;
        _longPollingReceiver = longPollingReceiver;
    }

    public async Task EnqueueUpdateAsync(Update update, CancellationToken cancellationToken)
    {
        _logger.LogDebug("Received update with type={Update}", update.Type.ToString());

        await _taskQueue.QueueBackgroundWorkItemAsync(
            async token => await HandleUpdateFunctionAsync(_telegramBotClient, update, token),
            cancellationToken);
    }

    private async Task HandleUpdateFunctionAsync(
        ITelegramBotClient botClient,
        Update update,
        CancellationToken cancellationToken)
    {
        try
        {
            if (update is { Type: UpdateType.Message, Message: { ForwardFrom: null, ForwardFromChat: null, ForwardSignature: null, From: not null, IsAutomaticForward: false } })
            {
                var chatId = update.Message.Chat.Id;
                var isAllowed = _options.Value.AllowedChatIds.Any(allowed => allowed == chatId);
                _logger.LogInformation(
                    "Message from chat {ChatId} (type={ChatType}, allowed={IsAllowed}). Allowed list: [{AllowedChatIds}]",
                    chatId,
                    update.Message.Chat.Type,
                    isAllowed,
                    string.Join(", ", _options.Value.AllowedChatIds));

                if (!isAllowed)
                {
                    return;
                }

                var author = update.Message.From.Username ?? string.Join(' ', update.Message.From.FirstName, update.Message.From.LastName).Trim();
                var message = (update.Message.Text
                               ?? update.Message.Caption
                               ?? update.Message.RichMessage.GetPlainText())
                    .Trim();
                var currentMention = Const.Mentions.FirstOrDefault(mention => message.StartsWithWholeWord(mention));

                if (message.StartsWithWholeWord(Const.DrawCommand))
                {
                    await _drawHandler.HandleDrawAsync(botClient, update.Message, message, author, cancellationToken);
                }
                else if (message.Equals(Const.ResetCommand, StringComparison.OrdinalIgnoreCase))
                {
                    await _resetHandler.HandleResetAsync(botClient, update.Message, message, author, cancellationToken);
                }
                else if (!string.IsNullOrEmpty(currentMention))
                {
                    var isGroupChat = update.Message.Chat.Type is ChatType.Group or ChatType.Supergroup;
                    await _mentionHandler.HandleMentionAsync(botClient, update.Message, message, isGroupChat, author, currentMention, cancellationToken);
                }
            }
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Error while handling update");
        }
    }

    /// <summary>
    /// With a webhook URL configured, registers the webhook and returns; otherwise long-polls
    /// until <paramref name="cancellationToken"/> is cancelled.
    /// </summary>
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        if (_options.Value.WebhookUrl is { } webhookUrl)
        {
            // A single connection makes Telegram deliver updates one by one and in order, as long polling does.
            await _telegramBotClient.SetWebhook(
                webhookUrl.AbsoluteUri,
                maxConnections: 1,
                allowedUpdates: AllowedUpdates,
                secretToken: _webhookSecret.Value,
                cancellationToken: cancellationToken);
            _logger.LogInformation("Receiving Telegram updates via webhook {WebhookUrl}", webhookUrl);
            return;
        }

        _logger.LogInformation("Receiving Telegram updates via long polling");
        await _longPollingReceiver.ReceiveAsync(this, AllowedUpdates, cancellationToken);
    }
}
