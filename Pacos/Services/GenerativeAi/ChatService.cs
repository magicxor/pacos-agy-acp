using System.Collections.Concurrent;
using System.Globalization;
using System.Text;
using Pacos.Models;
using Pacos.Services.Acp;

namespace Pacos.Services.GenerativeAi;

/// <summary>
/// Application-level facade over the agy-acp session pool. It builds the textual
/// prompt (persona lives in a per-chat steering file, tool instructions in per-chat
/// agent skills — see <see cref="ChatWorkspaceProvisioner"/>), shuttles attached files
/// through a per-turn scratch directory, and returns the agent's reply together
/// with any files it produced.
/// </summary>
public sealed class ChatService : IAsyncDisposable
{
    private readonly ILogger<ChatService> _logger;
    private readonly AcpSessionPool _sessionPool;
    private readonly ChatWorkspaceProvisioner _workspaceProvisioner;
    private readonly ConcurrentDictionary<long, SemaphoreSlim> _chatSemaphores = new();

    public ChatService(
        ILogger<ChatService> logger,
        AcpSessionPool sessionPool,
        ChatWorkspaceProvisioner workspaceProvisioner)
    {
        _logger = logger;
        _sessionPool = sessionPool;
        _workspaceProvisioner = workspaceProvisioner;
    }

    public async Task<ChatResponseInfo> GetResponseAsync(
        long chatId,
        bool isGroupChat,
        long messageId,
        string authorName,
        string messageText,
        IReadOnlyList<ChatInputFile>? attachments = null)
    {
        var chatSemaphore = GetOrCreateChatSemaphore(chatId);
        await chatSemaphore.WaitAsync();

        try
        {
            var workingDir = EnsureChatWorkspace(chatId, isGroupChat);

            using var workspace = TempWorkspace.Create(workingDir);

            var inputFiles = new List<(string Path, ChatInputOrigin Origin)>();
            if (attachments is not null)
            {
                foreach (var attachment in attachments)
                {
                    var resolvedName = ResolveFileName(attachment.MimeType, messageId, attachment.Origin);
                    var inputFilePath = workspace.WriteInputFile(attachment.Bytes, resolvedName);
                    inputFiles.Add((inputFilePath, attachment.Origin));
                    _logger.LogInformation("Stored attached file ({Origin}) for chat {ChatId} at {Path}", attachment.Origin, chatId, inputFilePath);
                }
            }

            var prompt = BuildPrompt(messageText, inputFiles, workspace.OutputDirectory, workspace.TempDirectory);

            var responseText = await _sessionPool.PromptAsync(chatId, prompt, CancellationToken.None);

            var outputFiles = workspace.CollectOutputFiles();
            if (outputFiles.Count > 0)
            {
                _logger.LogInformation("Agent produced {Count} output file(s) for chat {ChatId}", outputFiles.Count, chatId);
            }

            return new ChatResponseInfo(responseText.Trim(), outputFiles);
        }
        finally
        {
            chatSemaphore.Release();
        }
    }

    public async Task ResetChatHistoryAsync(long chatId)
    {
        var chatSemaphore = GetOrCreateChatSemaphore(chatId);
        await chatSemaphore.WaitAsync();

        try
        {
            await _sessionPool.ResetAsync(chatId);
            _logger.LogInformation("Chat history for chat ID {ChatId} has been reset", chatId);
        }
        finally
        {
            chatSemaphore.Release();
        }
    }

    private static string BuildPrompt(string messageText, IReadOnlyList<(string Path, ChatInputOrigin Origin)> inputFiles, string outputDirectory, string tempDirectory)
    {
        var builder = new StringBuilder();

        foreach (var (path, origin) in inputFiles)
        {
            var label = origin switch
            {
                ChatInputOrigin.RepliedMessage => "Файл из сообщения, на которое отвечает пользователь",
                _ => "Файл из сообщения пользователя",
            };
            builder.Append("[SYSTEM: ").Append(label).Append(": ").Append(path).AppendLine("]");
        }

        // Detailed file-delivery rules live in the steering file (AGENTS.md); here
        // we only point at this turn's output and temp directories.
        builder
            .Append("[SYSTEM: Выходная директория для файлов: ")
            .Append(outputDirectory)
            .AppendLine("]")
            .Append("[SYSTEM: Временная директория (не отправляется пользователю): ")
            .Append(tempDirectory)
            .AppendLine("]")
            .AppendLine();

        builder.Append(messageText);

        return builder.ToString();
    }

    private string EnsureChatWorkspace(long chatId, bool isGroupChat)
    {
        var workingDir = _sessionPool.GetWorkingDirectory(chatId);
        _workspaceProvisioner.Provision(workingDir, isGroupChat);

        return workingDir;
    }

    private static string ResolveFileName(string? mimeType, long messageId, ChatInputOrigin origin)
    {
        var prefix = origin switch
        {
            ChatInputOrigin.RepliedMessage => "replied_message",
            _ => "user_message",
        };
        var extension = MimeToExtension(mimeType);
        return $"{prefix}_{messageId.ToString(CultureInfo.InvariantCulture)}{extension}";
    }

    private static string MimeToExtension(string? mimeType) => mimeType?.ToLowerInvariant() switch
    {
        "image/jpeg" => ".jpg",
        "image/png" => ".png",
        "image/webp" => ".webp",
        "image/gif" => ".gif",
        "video/mp4" => ".mp4",
        "audio/mpeg" => ".mp3",
        "audio/ogg" => ".ogg",
        "application/pdf" => ".pdf",
        _ => ".bin",
    };

    private SemaphoreSlim GetOrCreateChatSemaphore(long chatId)
    {
        return _chatSemaphores.GetOrAdd(chatId, _ => new SemaphoreSlim(initialCount: 1, maxCount: 1));
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var semaphore in _chatSemaphores.Values)
        {
            semaphore.Dispose();
        }

        _chatSemaphores.Clear();

        await _sessionPool.DisposeAsync();
    }
}
