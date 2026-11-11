using Pacos.Models;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

namespace Pacos.Services;

public sealed class TelegramMediaService
{
    private readonly ILogger<TelegramMediaService> _logger;

    public TelegramMediaService(ILogger<TelegramMediaService> logger)
    {
        _logger = logger;
    }

    public static TelegramFileMetadata? GetFileMetadata(Message? message)
    {
        return message switch
        {
            { Photo: { } photos } when FromPhoto(photos) is { } photo => photo,
            { Video: { } video } => FromVideo(video),
            { VideoNote: { } videoNote } => new TelegramFileMetadata(videoNote.FileId, "video/mp4", videoNote.FileSize),
            { Audio: { } audio } => FromAudio(audio),
            { Voice: { } voice } => FromVoice(voice),
            { Animation: { } animation } => FromAnimation(animation),
            { Sticker: { IsVideo: true } sticker } => new TelegramFileMetadata(sticker.FileId, "video/webm", sticker.FileSize),
            // Animated (.tgs Lottie) stickers can't be fed to the model; use their static .webp/.jpg thumbnail,
            // which InputImageNormalizer re-encodes to PNG regardless of its actual format.
            { Sticker: { IsAnimated: true, Thumbnail: { } thumbnail } } => new TelegramFileMetadata(thumbnail.FileId, "image/webp", thumbnail.FileSize),
            { Sticker.IsAnimated: true } => null,
            { Sticker: { } sticker } => new TelegramFileMetadata(sticker.FileId, sticker.Type == StickerType.Regular ? "image/webp" : "application/octet-stream", sticker.FileSize),
            { Document: { } document } => FromDocument(document),
            _ => null,
        };
    }

    /// <summary>
    /// Returns every downloadable file of a message in reading order: its own media first, then
    /// the media embedded in the blocks of its rich message.
    /// </summary>
    public static IReadOnlyList<TelegramFileMetadata> GetAllFileMetadata(Message? message)
    {
        if (message is null)
        {
            return [];
        }

        var files = new List<TelegramFileMetadata>();

        if (GetFileMetadata(message) is { } ownFile)
        {
            files.Add(ownFile);
        }

        AddRichBlockFiles(files, message.RichMessage?.Blocks);

        // A rich message may show the same file more than once
        return [.. files.DistinctBy(file => file.FileId)];
    }

    public async Task<(byte[]? FileBytes, string? ErrorMessage)> DownloadMediaAsync(
        TelegramFileMetadata? fileMetadata,
        ITelegramBotClient botClient,
        CancellationToken cancellationToken)
    {
        var fileId = fileMetadata?.FileId;
        var fileSize = fileMetadata?.FileSize;

        if (string.IsNullOrEmpty(fileId))
            return (null, "No fileId provided for media download.");

        // 20 MB limit for media files
        const long maxFileSize = 20 * 1024 * 1024;

        if (fileSize is > maxFileSize)
        {
            return (null, $"File size {fileSize} bytes exceeds the maximum allowed size of {maxFileSize} bytes.");
        }

        _logger.LogDebug("Downloading media with fileId={FileId}", fileId);

        try
        {
            var fileInfo = await botClient.GetFile(fileId, cancellationToken);

            if (string.IsNullOrEmpty(fileInfo.FilePath))
            {
                _logger.LogWarning("FilePath is null or empty for fileId={FileId}. Cannot download media", fileId);
                return (null, "FilePath is null or empty. Cannot download media.");
            }

            var initialCapacity = fileSize is > 0 ? (int)Math.Min(fileSize.Value, maxFileSize) : 0;
            await using var memoryStream = initialCapacity > 0 ? new MemoryStream(initialCapacity) : new MemoryStream();
            await botClient.DownloadFile(fileInfo.FilePath, memoryStream, cancellationToken);
            var downloadedBytes = memoryStream.ToArray();
            _logger.LogInformation("Successfully downloaded media with fileId={FileId}, size={Size} bytes", fileId, downloadedBytes.Length);
            return (downloadedBytes, null);
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogError(ex, "Error downloading media with fileId={FileId}", fileId);
            return (null, ex.Message);
        }
    }

    private static void AddRichBlockFiles(List<TelegramFileMetadata> files, RichBlock[]? blocks)
    {
        if (blocks is null)
        {
            return;
        }

        foreach (var block in blocks)
        {
            switch (block)
            {
                case RichBlockCollage collage:
                    AddRichBlockFiles(files, collage.Blocks);
                    break;
                case RichBlockSlideshow slideshow:
                    AddRichBlockFiles(files, slideshow.Blocks);
                    break;
                case RichBlockBlockQuotation quotation:
                    AddRichBlockFiles(files, quotation.Blocks);
                    break;
                case RichBlockDetails details:
                    AddRichBlockFiles(files, details.Blocks);
                    break;
                case RichBlockList list:
                    foreach (var item in list.Items)
                    {
                        AddRichBlockFiles(files, item.Blocks);
                    }

                    break;
                default:
                    if (GetRichBlockFile(block) is { } file)
                    {
                        files.Add(file);
                    }

                    break;
            }
        }
    }

    private static TelegramFileMetadata? GetRichBlockFile(RichBlock block) => block switch
    {
        RichBlockPhoto { Photo: { } photos } => FromPhoto(photos),
        RichBlockVideo { Video: { } video } => FromVideo(video),
        RichBlockAnimation { Animation: { } animation } => FromAnimation(animation),
        RichBlockAudio { Audio: { } audio } => FromAudio(audio),
        RichBlockVoiceNote { VoiceNote: { } voice } => FromVoice(voice),
        RichBlockDocument { Document: { } document } => FromDocument(document),
        _ => null,
    };

    private static TelegramFileMetadata? FromPhoto(PhotoSize[] photos) =>
        photos.MaxBy(p => p.Width) is { } biggestPhoto
            ? new TelegramFileMetadata(biggestPhoto.FileId, "image/jpeg", biggestPhoto.FileSize)
            : null;

    private static TelegramFileMetadata FromVideo(Video video) =>
        new(video.FileId, video.MimeType ?? "video/mp4", video.FileSize);

    private static TelegramFileMetadata FromAnimation(Animation animation) =>
        new(animation.FileId, animation.MimeType ?? "video/mp4", animation.FileSize);

    private static TelegramFileMetadata FromAudio(Audio audio) =>
        new(audio.FileId, audio.MimeType ?? "audio/mpeg", audio.FileSize);

    private static TelegramFileMetadata FromVoice(Voice voice) =>
        new(voice.FileId, voice.MimeType ?? "audio/ogg", voice.FileSize);

    private static TelegramFileMetadata? FromDocument(Document document) =>
        string.IsNullOrEmpty(document.MimeType)
            ? null
            : new TelegramFileMetadata(document.FileId, document.MimeType, document.FileSize);
}
