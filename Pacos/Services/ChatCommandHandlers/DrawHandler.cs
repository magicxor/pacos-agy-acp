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

    public DrawHandler(
        ILogger<DrawHandler> logger,
        ChatService chatService,
        TelegramMediaService telegramMediaService)
    {
        _logger = logger;
        _chatService = chatService;
        _telegramMediaService = telegramMediaService;
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

        if (string.IsNullOrWhiteSpace(prompt) && userImageMetadata is null && repliedImageMetadata is null)
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
        var drawMessage = BuildDrawMessage(author, prompt, attachments.Count);

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

        var isFirstFile = true;
        foreach (var file in response.Files)
        {
            await using var stream = new MemoryStream(file.Content);
            var inputFile = new InputFileStream(stream, file.FileName);
            var fileCaption = isFirstFile && !string.IsNullOrWhiteSpace(caption) ? caption : null;
            isFirstFile = false;

            try
            {
                if (IsImageFile(file.FileName))
                {
                    await botClient.SendPhoto(
                        chatId: updateMessage.Chat.Id,
                        photo: inputFile,
                        caption: fileCaption,
                        replyParameters: new ReplyParameters { MessageId = updateMessage.MessageId },
                        cancellationToken: cancellationToken);
                }
                else
                {
                    await botClient.SendDocument(
                        chatId: updateMessage.Chat.Id,
                        document: inputFile,
                        caption: fileCaption,
                        replyParameters: new ReplyParameters { MessageId = updateMessage.MessageId },
                        cancellationToken: cancellationToken);
                }
            }
            catch (Exception e)
            {
                _logger.LogError(e, "Failed to send generated file {FileName} to {Author}", file.FileName, author);
            }
        }

        _logger.LogInformation("Sent {Count} generated file(s) to {Author}", response.Files.Count, author);
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

    private static string BuildDrawMessage(string author, string prompt, int imageCount)
    {
        var basis = imageCount switch
        {
            0 => "Сгенерируй изображение",
            1 => "Используя прикреплённое изображение как основу, сгенерируй новое изображение",
            _ => "Используя прикреплённые изображения как основу, сгенерируй новое изображение",
        };

        var request = string.IsNullOrWhiteSpace(prompt)
            ? $"{author}: {basis}."
            : $"{author}: {basis} по запросу: {prompt}.";

        return request + " Обязательно сохрани результат как файл изображения в выходную директорию.";
    }

    private static bool IsImageFile(string fileName)
    {
        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        return extension is ".jpg" or ".jpeg" or ".png" or ".webp" or ".gif";
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
