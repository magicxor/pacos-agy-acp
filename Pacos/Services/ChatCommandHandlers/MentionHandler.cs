using NTextCat;
using Pacos.Constants;
using Pacos.Extensions;
using Pacos.Models;
using Pacos.Services.Acp;
using Pacos.Services.GenerativeAi;
using Pacos.Services.Markdown;
using Pacos.Services.VideoConversion;
using Polly;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

namespace Pacos.Services.ChatCommandHandlers;

public sealed class MentionHandler
{
    private readonly ILogger<MentionHandler> _logger;
    private readonly RankedLanguageIdentifier _rankedLanguageIdentifier;
    private readonly ChatService _chatService;
    private readonly MarkdownConversionService _markdownConversionService;
    private readonly VideoConverter _videoConverter;
    private readonly TelegramMediaService _telegramMediaService;

    public MentionHandler(
        ILogger<MentionHandler> logger,
        RankedLanguageIdentifier rankedLanguageIdentifier,
        ChatService chatService,
        MarkdownConversionService markdownConversionService,
        VideoConverter videoConverter,
        TelegramMediaService telegramMediaService)
    {
        _logger = logger;
        _rankedLanguageIdentifier = rankedLanguageIdentifier;
        _chatService = chatService;
        _markdownConversionService = markdownConversionService;
        _videoConverter = videoConverter;
        _telegramMediaService = telegramMediaService;
    }

    /// <summary>
    /// Produces a human-readable description of where a forwarded message originally came from,
    /// or <c>null</c> if the message is not a forward (or the origin type is unsupported).
    /// </summary>
    private static string? DescribeForwardOrigin(MessageOrigin? origin) => origin switch
    {
        MessageOriginUser user => DescribeUser(user.SenderUser),
        MessageOriginHiddenUser hiddenUser => string.IsNullOrWhiteSpace(hiddenUser.SenderUserName)
            ? "a hidden user"
            : $"user {hiddenUser.SenderUserName}",
        MessageOriginChannel channel => string.IsNullOrWhiteSpace(channel.AuthorSignature)
            ? $"channel \"{channel.Chat.Title}\""
            : $"channel \"{channel.Chat.Title}\" ({channel.AuthorSignature})",
        MessageOriginChat chat => string.IsNullOrWhiteSpace(chat.AuthorSignature)
            ? $"group \"{chat.SenderChat.Title}\""
            : $"group \"{chat.SenderChat.Title}\" ({chat.AuthorSignature})",
        _ => null,
    };

    private static string DescribeUser(User user)
    {
        var name = user.Username ?? string.Join(' ', user.FirstName, user.LastName).Trim();
        return string.IsNullOrWhiteSpace(name) ? "an unknown user" : $"user {name}";
    }

    private async Task<ChatResponseInfo> GetChatResponseWithRetryAsync(
        long chatId,
        bool isGroupChat,
        long messageId,
        string authorName,
        string messageText,
        byte[]? fileBytes = null,
        string? fileMimeType = null)
    {
        var retryPolicy = Policy
            .Handle<TimeoutException>()
            .Or<AcpException>()
            .Or<HttpRequestException>()
            .Or<IOException>()
            .OrResult<ChatResponseInfo>(x => string.IsNullOrWhiteSpace(x.Text) && x.Files.Count == 0)
            .WaitAndRetryAsync(retryCount: 1, retryNumber => TimeSpan.FromMilliseconds(retryNumber * 200));

        return await retryPolicy.ExecuteAsync(() => _chatService.GetResponseAsync(
            chatId,
            isGroupChat,
            messageId,
            authorName,
            messageText,
            fileBytes,
            fileMimeType
        ));
    }

    private static string? PollToText(Poll? poll)
    {
        if (poll is null)
        {
            return null;
        }

        var optionsText = string.Join(", ", poll.Options.Select((o, i) => $"{i + 1}) {o.Text}"));
        return $"Poll: {poll.Question} | Description: {poll.Description} | Options: {optionsText}";
    }

    private static string? RichMessageToText(RichMessage? richMessage)
    {
        return richMessage?.GetPlainText();
    }

    public async Task HandleMentionAsync(
        ITelegramBotClient botClient,
        Message updateMessage,
        string messageText,
        bool isGroupChat,
        string author,
        string currentMention,
        CancellationToken cancellationToken)
    {
        // Remove the mention from the message
        messageText = messageText.Substring(currentMention.Length).TrimStart(',', ' ', '.', '!', '?', ':', ';').Trim();

        // Check for replied-to message text
        var repliedToMessageText = (updateMessage.ReplyToMessage?.Text
                                    ?? updateMessage.ReplyToMessage?.Caption
                                    ?? PollToText(updateMessage.ReplyToMessage?.Poll)
                                    ?? RichMessageToText(updateMessage.ReplyToMessage?.RichMessage)
                                    ?? string.Empty)
            .Trim();

        // Only exit early if both message and replied-to message are empty
        if (string.IsNullOrEmpty(messageText) && string.IsNullOrEmpty(repliedToMessageText))
        {
            return;
        }

        var language = _rankedLanguageIdentifier.Identify(messageText).FirstOrDefault();
        var languageCode = language?.Item1?.Iso639_3 ?? "eng";

        // Determine the full message to send to the LLM, including replied-to message if present
        string fullMessageToLlm;
        string originalMessageLogInfo = string.Empty;

        if (updateMessage.ReplyToMessage != null)
        {
            // repliedToMessageText was already computed above (incl. Poll/RichMessage fallbacks) — reuse it here.
            if (!string.IsNullOrEmpty(repliedToMessageText))
            {
                var repliedToAuthor = updateMessage.ReplyToMessage.From?.Username ??
                                      string.Join(' ', updateMessage.ReplyToMessage.From?.FirstName, updateMessage.ReplyToMessage.From?.LastName).Trim();
                if (string.IsNullOrWhiteSpace(repliedToAuthor))
                {
                    repliedToAuthor = "Original Poster"; // Fallback if author is not available
                }

                // If the replied-to message is itself a forward, surface its original source to the LLM
                var forwardSource = DescribeForwardOrigin(updateMessage.ReplyToMessage.ForwardOrigin);
                var originalMessageHeader = forwardSource != null
                    ? $"--- Original Message by {repliedToAuthor} (forwarded from {forwardSource}): ---"
                    : $"--- Original Message by {repliedToAuthor}: ---";

                fullMessageToLlm = $"{author} (replying to {repliedToAuthor}): {messageText}\n\n{originalMessageHeader}\n{repliedToMessageText}";
                originalMessageLogInfo = forwardSource != null
                    ? $" | Original by {repliedToAuthor} (forwarded from {forwardSource}): \"{repliedToMessageText.Cut(50)}\"" // Cut for brevity in logs
                    : $" | Original by {repliedToAuthor}: \"{repliedToMessageText.Cut(50)}\""; // Cut for brevity in logs
            }
            else
            {
                fullMessageToLlm = $"{author}: {messageText}"; // ReplyToMessage exists but has no text/caption
            }
        }
        else
        {
            fullMessageToLlm = $"{author}: {messageText}"; // Not a reply
        }

        _logger.LogInformation("Processing prompt from {Author} (lang={LanguageCode}): \"{UserMessage}\"{OriginalMessageLog}",
            author,
            languageCode,
            messageText,
            originalMessageLogInfo);

        var fileMetadata = TelegramMediaService.GetFileMetadata(updateMessage) ?? TelegramMediaService.GetFileMetadata(updateMessage.ReplyToMessage);
        _logger.LogInformation("Media info for message from {Author}: FileId={FileId}, MimeType={MimeType}",
            author,
            fileMetadata?.FileId,
            fileMetadata?.MimeType);

        var media = await _telegramMediaService.DownloadMediaAsync(fileMetadata, botClient, cancellationToken);

        const int maxFileSize = 10_000_000;
        if (fileMetadata?.MimeType.StartsWith("video/", StringComparison.OrdinalIgnoreCase) == true && media.FileBytes is not null)
        {
            try
            {
                media.FileBytes = await _videoConverter.ConvertAsync(media.FileBytes, maxFileSize, cancellationToken);
            }
            catch (Exception e)
            {
                _logger.LogError(e, "Failed to convert video for {Author}. Error: {ErrorMessage}", author, e.Message);
                media.FileBytes = null;
                media.ErrorMessage = $"Video conversion failed: {e.Message}";
            }
        }

        if (fileMetadata?.FileId is not null && media.FileBytes is null && media.ErrorMessage is not null)
        {
            fullMessageToLlm = $"{fullMessageToLlm}\n\n[Media download error: {media.ErrorMessage}]";
        }

        ChatResponseInfo response;

        try
        {
            response = await GetChatResponseWithRetryAsync(
                updateMessage.Chat.Id,
                isGroupChat,
                updateMessage.Id,
                author,
                fullMessageToLlm,
                media.FileBytes,
                fileMetadata?.MimeType
            );
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Failed to get chat response for {Author}", author);
            response = new ChatResponseInfo($"{e.GetType().Name}: {e.Message}", []);
        }

        var replyText = response.Text.Cut(Const.MaxTelegramMessageLength);

        if (string.IsNullOrWhiteSpace(replyText) && response.Files.Count == 0)
        {
            replyText = "Error: Received empty response from chat service.";
        }

        if (!string.IsNullOrWhiteSpace(replyText))
        {
            var markdownReplyText = _markdownConversionService.ConvertToTelegramMarkdown(replyText);

            _logger.LogInformation("Replying to {Author} with: {ReplyText}", author, replyText);

            try
            {
                await SendReply(markdownReplyText, ParseMode.MarkdownV2);
            }
            catch (Exception e)
            {
                _logger.LogError(e, "Failed to send message with MarkdownV2. Falling back to plain text");
                await SendReply(replyText, ParseMode.None);
            }
        }

        foreach (var file in response.Files)
        {
            try
            {
                await SendOutputFile(file);
            }
            catch (Exception e)
            {
                _logger.LogError(e, "Failed to send output file {FileName} to {Author}", file.FileName, author);
            }
        }

        return;

        async Task SendReply(string text, ParseMode parseMode)
        {
            await botClient.SendMessage(
                new ChatId(updateMessage.Chat.Id),
                text,
                parseMode,
                new ReplyParameters { MessageId = updateMessage.MessageId, },
                cancellationToken: cancellationToken);
        }

        async Task SendOutputFile(OutputFile file)
        {
            await using var stream = new MemoryStream(file.Content);
            var inputFile = new InputFileStream(stream, file.FileName);

            if (IsImageFile(file.FileName))
            {
                await botClient.SendPhoto(
                    updateMessage.Chat.Id,
                    inputFile,
                    replyParameters: new ReplyParameters { MessageId = updateMessage.MessageId },
                    cancellationToken: cancellationToken);
            }
            else
            {
                await botClient.SendDocument(
                    updateMessage.Chat.Id,
                    inputFile,
                    replyParameters: new ReplyParameters { MessageId = updateMessage.MessageId },
                    cancellationToken: cancellationToken);
            }
        }
    }

    private static bool IsImageFile(string fileName)
    {
        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        return extension is ".jpg" or ".jpeg" or ".png" or ".webp" or ".gif";
    }
}
