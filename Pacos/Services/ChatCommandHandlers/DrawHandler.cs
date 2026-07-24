using Pacos.Constants;
using Pacos.Extensions;
using Pacos.Models;
using Pacos.Services.GenerativeAi;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

namespace Pacos.Services.ChatCommandHandlers;

public sealed class DrawHandler
{
    private readonly ILogger<DrawHandler> _logger;
    private readonly ChatService _chatService;
    private readonly TelegramMediaService _telegramMediaService;
    private readonly OutputFileSender _outputFileSender;

    public DrawHandler(
        ILogger<DrawHandler> logger,
        ChatService chatService,
        TelegramMediaService telegramMediaService,
        OutputFileSender outputFileSender)
    {
        _logger = logger;
        _chatService = chatService;
        _telegramMediaService = telegramMediaService;
        _outputFileSender = outputFileSender;
    }

    public async Task HandleDrawAsync(
        ITelegramBotClient botClient,
        Message updateMessage,
        string messageText,
        string author,
        CancellationToken cancellationToken)
    {
        var prompt = messageText.Substring(Const.DrawCommand.Length).Trim();
        _logger.LogInformation("Processing {Command} command from {Author} with prompt: {Prompt}", Const.DrawCommand, author, prompt);

        // Locate optional source images: the user's own message and the post it replies to.
        var userImageMetadata = GetImageMetadata(updateMessage);
        var repliedImageMetadata = updateMessage.ReplyToMessage is not null
            ? GetImageMetadata(updateMessage.ReplyToMessage)
            : null;

        // A bare command replying to a message is a request to visualize that message,
        // so its text counts as a prompt source too.
        var repliedToMessageText = (updateMessage.ReplyToMessage?.Text
                                    ?? updateMessage.ReplyToMessage?.Caption
                                    ?? updateMessage.ReplyToMessage?.RichMessage.GetPlainText()
                                    ?? string.Empty)
            .Trim();

        if (string.IsNullOrWhiteSpace(prompt)
            && repliedToMessageText.Length == 0
            && userImageMetadata is null
            && repliedImageMetadata is null)
        {
            await botClient.SendMessage(
                chatId: updateMessage.Chat.Id,
                text: $"Please provide a prompt for {Const.DrawCommand}. Example: {Const.DrawCommand} a cat wearing a hat",
                replyParameters: new ReplyParameters { MessageId = updateMessage.MessageId },
                cancellationToken: cancellationToken);
            return;
        }

        // Download both images best-effort; a single failed download must not block generation.
        var attachments = new List<ChatInputFile>();
        await AddImageAsync(userImageMetadata, ChatInputOrigin.UserMessage);
        await AddImageAsync(repliedImageMetadata, ChatInputOrigin.RepliedMessage);

        var isGroupChat = updateMessage.Chat.Type is ChatType.Group or ChatType.Supergroup;
        var drawMessage = BuildDrawMessage(author, prompt, repliedToMessageText, attachments.Count);

        ChatResponseInfo response;
        try
        {
            response = await _chatService.GetResponseAsync(
                updateMessage.Chat.Id,
                isGroupChat,
                updateMessage.Id,
                author,
                drawMessage,
                attachments);
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Failed to generate image for {Author}", author);
            await botClient.SendMessage(
                chatId: updateMessage.Chat.Id,
                text: $"{e.GetType().Name}: {e.Message}",
                replyParameters: new ReplyParameters { MessageId = updateMessage.MessageId },
                cancellationToken: cancellationToken);
            return;
        }

        var caption = response.Text.Cut(Const.MaxTelegramCaptionLength);

        if (response.Files.Count == 0)
        {
            await botClient.SendMessage(
                chatId: updateMessage.Chat.Id,
                text: !string.IsNullOrWhiteSpace(caption) ? caption : "Sorry, couldn't generate an image.",
                replyParameters: new ReplyParameters { MessageId = updateMessage.MessageId },
                cancellationToken: cancellationToken);
            return;
        }

        var captionText = !string.IsNullOrWhiteSpace(caption) ? caption : null;
        var sentCount = await _outputFileSender.SendAsync(
            botClient,
            updateMessage.Chat.Id,
            updateMessage.MessageId,
            response.Files,
            captionText,
            cancellationToken);

        _logger.LogInformation("Sent {Count} generated file(s) to {Author}", sentCount, author);
        return;

        async Task AddImageAsync(TelegramFileMetadata? metadata, ChatInputOrigin origin)
        {
            if (metadata is null)
            {
                return;
            }

            var (imageBytes, downloadError) = await _telegramMediaService.DownloadMediaAsync(metadata, botClient, cancellationToken);
            if (imageBytes is null)
            {
                _logger.LogWarning("Failed to download {Origin} image for {Author}: {Error}", origin, author, downloadError);
                return;
            }

            attachments.Add(new ChatInputFile(imageBytes, metadata.MimeType, origin));
        }
    }

    internal static string BuildDrawMessage(string author, string prompt, string repliedToMessageText, int imageCount)
    {
        var basis = imageCount switch
        {
            0 => "Сгенерируй изображение",
            1 => "Используя прикреплённое изображение как основу, сгенерируй новое изображение",
            _ => "Используя прикреплённые изображения как основу, сгенерируй новое изображение",
        };

        string request;
        if (!string.IsNullOrWhiteSpace(prompt))
        {
            request = $"{author}: {basis} по запросу: {prompt}.";
        }
        else if (repliedToMessageText.Length > 0)
        {
            request = $"{author}: {basis}, визуализирующее следующее сообщение: {repliedToMessageText}.";
        }
        else
        {
            request = $"{author}: {basis}.";
        }

        return request + " Обязательно сохрани результат как файл изображения в выходную директорию.";
    }

    /// <summary>
    /// Gets image metadata from a message (Photo or Sticker only).
    /// Returns null for other media types.
    /// </summary>
    private static TelegramFileMetadata? GetImageMetadata(Message message)
    {
        var metadata = TelegramMediaService.GetFileMetadata(message);
        return metadata?.MimeType.StartsWith("image/", StringComparison.OrdinalIgnoreCase) == true
            ? metadata
            : null;
    }
}
