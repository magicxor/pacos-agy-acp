using Pacos.Constants;
using Pacos.Models;
using Pacos.Services.ImageConversion;
using Telegram.Bot;
using Telegram.Bot.Types;

namespace Pacos.Services;

/// <summary>
/// Delivers the files an agent produced in its output directory to Telegram.
/// Images (JPG/JPEG/PNG/GIF/WebP) and MP4 videos are grouped into a single
/// media album; every other file is grouped into a single document group. An
/// image whose aspect ratio is too extreme for Telegram to accept as a photo
/// (longer side more than <see cref="Const.MaxTelegramPhotoMaxAspectRatio"/>× the
/// shorter one) is sent as a document instead and consequently loses any NSFW
/// spoiler, which Telegram does not support for documents. Each group is capped
/// at <see cref="Const.MaxTelegramMediaGroupSize"/> items (dropping the
/// alphabetically-last overflow), and any image/video whose name contains "nsfw"
/// is covered with a spoiler. Videos and documents larger than
/// <see cref="Const.MaxTelegramFileSizeBytes"/> are dropped with a warning,
/// since (unlike photos) they cannot be downscaled to fit Telegram's limit.
/// </summary>
public sealed class OutputFileSender
{
    private const string NsfwMarker = "nsfw";

    private static readonly string[] ImageExtensions = [".jpg", ".jpeg", ".png", ".gif", ".webp"];
    private static readonly string[] VideoExtensions = [".mp4"];

    private readonly ILogger<OutputFileSender> _logger;
    private readonly ImageDownscaler _imageDownscaler;

    public OutputFileSender(ILogger<OutputFileSender> logger, ImageDownscaler imageDownscaler)
    {
        _logger = logger;
        _imageDownscaler = imageDownscaler;
    }

    /// <summary>
    /// Sends <paramref name="files"/> as a media album and/or a document group,
    /// applying the per-group cap and NSFW spoiler rules. Returns the number of
    /// files actually delivered.
    /// </summary>
    public async Task<int> SendAsync(
        ITelegramBotClient botClient,
        long chatId,
        int replyToMessageId,
        IReadOnlyCollection<OutputFile> files,
        string? caption,
        CancellationToken cancellationToken)
    {
        var imageDimensions = MeasureImages(files);

        // An image whose aspect ratio Telegram rejects for a photo (longer side more
        // than MaxTelegramPhotoMaxAspectRatio× the shorter one) is routed into the
        // document group instead. Such an image therefore loses any NSFW spoiler,
        // which Telegram does not support for documents.
        var (media, documents, droppedMedia, droppedDocuments, oversized) = BuildPlan(
            files,
            file => imageDimensions.TryGetValue(file, out var dimensions)
                && ImageDownscaler.ExceedsAspectRatioLimit(dimensions.Width, dimensions.Height, Const.MaxTelegramPhotoMaxAspectRatio));

        media = [.. media.Select(item => DownscaleIfOversized(item, imageDimensions))];

        LogOversizedFiles(oversized);
        LogDroppedFiles("images/videos", media.Select(static m => m.File.FileName), droppedMedia);
        LogDroppedFiles("documents", documents.Select(static f => f.FileName), droppedDocuments);

        var replyParameters = new ReplyParameters { MessageId = replyToMessageId, };

        // The caption belongs on the first item of the first non-empty group.
        var mediaCaption = media.Count > 0 ? caption : null;
        var documentCaption = media.Count == 0 ? caption : null;

        var sentCount = await SendMediaAsync(botClient, chatId, replyParameters, media, mediaCaption, cancellationToken);
        sentCount += await SendDocumentsAsync(botClient, chatId, replyParameters, documents, documentCaption, cancellationToken);

        return sentCount;
    }

    /// <summary>
    /// Splits the output files into an ordered, capped media group and document
    /// group, flagging spoilers. Pure and side-effect free so it can be tested
    /// without a Telegram client.
    /// </summary>
    internal static (IReadOnlyList<PlannedMedia> Media,
                     IReadOnlyList<OutputFile> Documents,
                     IReadOnlyList<OutputFile> DroppedMedia,
                     IReadOnlyList<OutputFile> DroppedDocuments,
                     IReadOnlyList<OutputFile> DroppedOversized)
        BuildPlan(IReadOnlyCollection<OutputFile> files, Func<OutputFile, bool>? shouldSendImageAsDocument = null)
    {
        // The IsImage guard keeps the reclassification confined to images, so a video
        // is never rerouted into the document group regardless of what the predicate returns.
        Func<OutputFile, bool> forceDocument = shouldSendImageAsDocument ?? (static _ => false);

        var mediaFiles = files
            .Where(f => IsMedia(f.FileName) && !(IsImage(f.FileName) && forceDocument(f)))
            .OrderBy(static f => f.FileName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var documentFiles = files
            .Where(f => !IsMedia(f.FileName) || (IsImage(f.FileName) && forceDocument(f)))
            .OrderBy(static f => f.FileName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        // Photos are downscaled to fit Telegram's limits, but videos and documents
        // cannot be shrunk, so any that exceed the upload limit are dropped outright.
        var oversized = mediaFiles
            .Where(static f => IsVideo(f.FileName) && ExceedsUploadLimit(f))
            .Concat(documentFiles.Where(static f => ExceedsUploadLimit(f)))
            .OrderBy(static f => f.FileName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var keptMediaFiles = mediaFiles
            .Where(static f => !IsVideo(f.FileName) || !ExceedsUploadLimit(f))
            .ToList();
        var keptDocumentFiles = documentFiles
            .Where(static f => !ExceedsUploadLimit(f))
            .ToList();

        var media = keptMediaFiles
            .Take(Const.MaxTelegramMediaGroupSize)
            .Select(static f => new PlannedMedia(f, GetMediaKind(f.FileName), HasNsfwMarker(f.FileName)))
            .ToList();
        var droppedMedia = keptMediaFiles.Skip(Const.MaxTelegramMediaGroupSize).ToList();

        var documents = keptDocumentFiles.Take(Const.MaxTelegramMediaGroupSize).ToList();
        var droppedDocuments = keptDocumentFiles.Skip(Const.MaxTelegramMediaGroupSize).ToList();

        return (media, documents, droppedMedia, droppedDocuments, oversized);
    }

    private static bool IsMedia(string fileName) => IsImage(fileName) || IsVideo(fileName);

    private static bool IsImage(string fileName) => ImageExtensions.Contains(Path.GetExtension(fileName).ToLowerInvariant());

    private static bool IsVideo(string fileName) => VideoExtensions.Contains(Path.GetExtension(fileName).ToLowerInvariant());

    private static OutputMediaKind GetMediaKind(string fileName) => IsVideo(fileName) ? OutputMediaKind.Video : OutputMediaKind.Photo;

    private static bool HasNsfwMarker(string fileName) => fileName.Contains(NsfwMarker, StringComparison.OrdinalIgnoreCase);

    private static bool ExceedsUploadLimit(OutputFile file) => file.Content.Length > Const.MaxTelegramFileSizeBytes;

    // A photo must be downscaled when it is too large in bytes, or when its
    // width + height exceeds Telegram's photo semiperimeter limit (which can happen
    // well under the byte limit). FitWithinBounds fits it inside 2560x2560, which
    // also flattens an animated GIF/WebP to a static JPEG.
    private static bool NeedsDownscaling(
        OutputFile file,
        IReadOnlyDictionary<OutputFile, (int Width, int Height)> imageDimensions) =>
        file.Content.Length > Const.MaxTelegramPhotoSizeBytes
        || (imageDimensions.TryGetValue(file, out var dimensions)
            && ImageDownscaler.ExceedsSemiperimeter(dimensions.Width, dimensions.Height, Const.MaxTelegramPhotoSemiperimeter));

    private static IAlbumInputMedia CreateAlbumMedia(PlannedMedia item, InputFile inputFile, string? caption)
    {
        if (item.Kind == OutputMediaKind.Video)
        {
            return new InputMediaVideo(inputFile) { Caption = caption, HasSpoiler = item.HasSpoiler, };
        }

        return new InputMediaPhoto(inputFile) { Caption = caption, HasSpoiler = item.HasSpoiler, };
    }

    private static async Task SendSingleMediaAsync(
        ITelegramBotClient botClient,
        long chatId,
        ReplyParameters replyParameters,
        PlannedMedia item,
        string? caption,
        CancellationToken cancellationToken)
    {
        await using var stream = new MemoryStream(item.File.Content);
        var inputFile = new InputFileStream(stream, item.File.FileName);

        if (item.Kind == OutputMediaKind.Video)
        {
            await botClient.SendVideo(
                chatId,
                inputFile,
                caption: caption,
                hasSpoiler: item.HasSpoiler,
                replyParameters: replyParameters,
                cancellationToken: cancellationToken);
            return;
        }

        await botClient.SendPhoto(
            chatId,
            inputFile,
            caption: caption,
            hasSpoiler: item.HasSpoiler,
            replyParameters: replyParameters,
            cancellationToken: cancellationToken);
    }

    private static async Task SendMediaGroupAsync(
        ITelegramBotClient botClient,
        long chatId,
        ReplyParameters replyParameters,
        IReadOnlyList<PlannedMedia> media,
        string? caption,
        CancellationToken cancellationToken)
    {
        var streams = new List<MemoryStream>(media.Count);
        try
        {
            var album = new List<IAlbumInputMedia>(media.Count);
            for (var i = 0; i < media.Count; i++)
            {
                var item = media[i];
                var stream = new MemoryStream(item.File.Content);
                streams.Add(stream);
                var inputFile = new InputFileStream(stream, item.File.FileName);
                album.Add(CreateAlbumMedia(item, inputFile, i == 0 ? caption : null));
            }

            await botClient.SendMediaGroup(
                chatId,
                album,
                replyParameters: replyParameters,
                cancellationToken: cancellationToken);
        }
        finally
        {
            foreach (var stream in streams)
            {
                await stream.DisposeAsync();
            }
        }
    }

    private static async Task SendSingleDocumentAsync(
        ITelegramBotClient botClient,
        long chatId,
        ReplyParameters replyParameters,
        OutputFile document,
        string? caption,
        CancellationToken cancellationToken)
    {
        await using var stream = new MemoryStream(document.Content);
        var inputFile = new InputFileStream(stream, document.FileName);

        await botClient.SendDocument(
            chatId,
            inputFile,
            caption: caption,
            replyParameters: replyParameters,
            cancellationToken: cancellationToken);
    }

    private static async Task SendDocumentGroupAsync(
        ITelegramBotClient botClient,
        long chatId,
        ReplyParameters replyParameters,
        IReadOnlyList<OutputFile> documents,
        string? caption,
        CancellationToken cancellationToken)
    {
        var streams = new List<MemoryStream>(documents.Count);
        try
        {
            var album = new List<IAlbumInputMedia>(documents.Count);
            for (var i = 0; i < documents.Count; i++)
            {
                var document = documents[i];
                var stream = new MemoryStream(document.Content);
                streams.Add(stream);
                var inputFile = new InputFileStream(stream, document.FileName);
                album.Add(new InputMediaDocument(inputFile) { Caption = i == 0 ? caption : null, });
            }

            await botClient.SendMediaGroup(
                chatId,
                album,
                replyParameters: replyParameters,
                cancellationToken: cancellationToken);
        }
        finally
        {
            foreach (var stream in streams)
            {
                await stream.DisposeAsync();
            }
        }
    }

    // Keyed by the OutputFile instance rather than its file name: CollectOutputFiles
    // flattens subdirectories to base names, so images from different folders can
    // share a name; keying by name would let one image's dimensions mask another's.
    private Dictionary<OutputFile, (int Width, int Height)> MeasureImages(IReadOnlyCollection<OutputFile> files)
    {
        var dimensions = new Dictionary<OutputFile, (int Width, int Height)>();
        foreach (var file in files.Where(static f => IsImage(f.FileName)))
        {
            if (_imageDownscaler.TryGetDimensions(file) is { } size)
            {
                dimensions[file] = size;
            }
        }

        return dimensions;
    }

    // Extreme-aspect images are routed to the document group and never reach this
    // method, so they are always sent full-resolution: downscaling them cannot make
    // them a valid photo (2560x2560 preserves the aspect ratio) and would only
    // discard detail.
    private PlannedMedia DownscaleIfOversized(
        PlannedMedia item,
        IReadOnlyDictionary<OutputFile, (int Width, int Height)> imageDimensions) =>
        item.Kind == OutputMediaKind.Photo && NeedsDownscaling(item.File, imageDimensions)
            ? item with { File = _imageDownscaler.FitWithinBounds(item.File) }
            : item;

    private async Task<int> SendMediaAsync(
        ITelegramBotClient botClient,
        long chatId,
        ReplyParameters replyParameters,
        IReadOnlyList<PlannedMedia> media,
        string? caption,
        CancellationToken cancellationToken)
    {
        if (media.Count == 0)
        {
            return 0;
        }

        try
        {
            if (media.Count == 1)
            {
                await SendSingleMediaAsync(botClient, chatId, replyParameters, media[0], caption, cancellationToken);
            }
            else
            {
                await SendMediaGroupAsync(botClient, chatId, replyParameters, media, caption, cancellationToken);
            }

            return media.Count;
        }
        catch (Exception e)
        {
            _logger.LogError(
                e,
                "Failed to send {Count} media file(s) to chat {ChatId}: [{Files}]",
                media.Count,
                chatId,
                string.Join(", ", media.Select(static m => m.File.FileName)));
            return 0;
        }
    }

    private async Task<int> SendDocumentsAsync(
        ITelegramBotClient botClient,
        long chatId,
        ReplyParameters replyParameters,
        IReadOnlyList<OutputFile> documents,
        string? caption,
        CancellationToken cancellationToken)
    {
        if (documents.Count == 0)
        {
            return 0;
        }

        try
        {
            if (documents.Count == 1)
            {
                await SendSingleDocumentAsync(botClient, chatId, replyParameters, documents[0], caption, cancellationToken);
            }
            else
            {
                await SendDocumentGroupAsync(botClient, chatId, replyParameters, documents, caption, cancellationToken);
            }

            return documents.Count;
        }
        catch (Exception e)
        {
            _logger.LogError(
                e,
                "Failed to send {Count} document(s) to chat {ChatId}: [{Files}]",
                documents.Count,
                chatId,
                string.Join(", ", documents.Select(static f => f.FileName)));
            return 0;
        }
    }

    private void LogOversizedFiles(IReadOnlyCollection<OutputFile> oversized)
    {
        if (oversized.Count == 0)
        {
            return;
        }

        _logger.LogWarning(
            "Dropped {Count} output file(s) exceeding Telegram's upload limit of {LimitBytes} bytes: [{Files}]",
            oversized.Count,
            Const.MaxTelegramFileSizeBytes,
            string.Join(", ", oversized.Select(static f => $"{f.FileName} ({f.Content.Length} bytes)")));
    }

    private void LogDroppedFiles(string category, IEnumerable<string> sentNames, IReadOnlyCollection<OutputFile> dropped)
    {
        if (dropped.Count == 0)
        {
            return;
        }

        var sent = sentNames.ToList();
        _logger.LogWarning(
            "Output {Category} exceeded the Telegram group limit of {Limit}; sending {SentCount} file(s) and dropping {DroppedCount}. Sent: [{SentFiles}]. Dropped: [{DroppedFiles}]",
            category,
            Const.MaxTelegramMediaGroupSize,
            sent.Count,
            dropped.Count,
            string.Join(", ", sent),
            string.Join(", ", dropped.Select(static f => f.FileName)));
    }
}
