using System.Collections;
using System.Collections.Concurrent;
using System.Globalization;
using Microsoft.Extensions.Options;
using Pacos.Models.Options;

namespace Pacos.Services.Acp;

/// <summary>
/// Owns one long-lived <see cref="AcpConnection"/> (and therefore one agy
/// conversation) per Telegram chat. agy-acp handles a single prompt at a time
/// per process, so each chat is served by its own dedicated process whose
/// working directory is unique to that chat.
/// </summary>
public sealed class AcpSessionPool : IAsyncDisposable
{
    private readonly ILogger<AcpSessionPool> _logger;
    private readonly string _command;
    private readonly IReadOnlyList<string> _args;
    private readonly string _root;
    private readonly TimeSpan _promptTimeout;
    private readonly IReadOnlyDictionary<string, string?> _environment;

    private readonly ConcurrentDictionary<long, ChatSession> _sessions = new();
    private readonly ConcurrentDictionary<long, SemaphoreSlim> _locks = new();

    public AcpSessionPool(ILogger<AcpSessionPool> logger, IOptions<PacosOptions> options)
    {
        _logger = logger;

        var value = options.Value;
        _command = value.AgyAcpCommand;
        _args = value.AgyAcpArgs;
        _promptTimeout = TimeSpan.FromSeconds(value.PromptTimeoutSeconds);
        _root = ResolveRoot(value);
        _environment = BuildEnvironment(value);
    }

    /// <summary>
    /// Resolves the root directory under which per-chat working directories live.
    /// Shared with the security policy so the MCP filesystem allow-list matches.
    /// </summary>
    public static string ResolveRoot(PacosOptions options)
    {
        return string.IsNullOrWhiteSpace(options.WorkingDirectoryRoot)
            ? Path.Combine(Path.GetTempPath(), "pacos-agy")
            : options.WorkingDirectoryRoot;
    }

    /// <summary>
    /// Returns the per-chat working directory (created lazily on first prompt).
    /// This is the directory agy treats as the workspace and where steering files
    /// and per-turn temporary folders live.
    /// </summary>
    public string GetWorkingDirectory(long chatId)
    {
        return Path.Combine(_root, chatId.ToString(CultureInfo.InvariantCulture));
    }

    /// <summary>
    /// Sends a prompt for the given chat, creating or reusing its dedicated
    /// agy-acp session. On timeout or protocol error the session is torn down so
    /// the next prompt starts a fresh process.
    /// </summary>
    public async Task<string> PromptAsync(long chatId, string promptText, CancellationToken cancellationToken)
    {
        var gate = _locks.GetOrAdd(chatId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            var session = await EnsureSessionAsync(chatId, cancellationToken);
            try
            {
                return await session.Connection.PromptAsync(
                    session.SessionId,
                    promptText,
                    _promptTimeout,
                    cancellationToken);
            }
            catch (Exception e) when (e is TimeoutException or AcpException)
            {
                _logger.LogWarning(e, "agy-acp prompt failed for chat {ChatId}; tearing down session", chatId);
                await RemoveSessionAsync(chatId);
                throw;
            }
        }
        finally
        {
            gate.Release();
        }
    }

    /// <summary>
    /// Tears down the agy-acp session for a chat so the next prompt begins a new
    /// conversation with no memory of prior turns.
    /// </summary>
    public async Task ResetAsync(long chatId)
    {
        var gate = _locks.GetOrAdd(chatId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync();
        try
        {
            await RemoveSessionAsync(chatId);
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task<ChatSession> EnsureSessionAsync(long chatId, CancellationToken cancellationToken)
    {
        if (_sessions.TryGetValue(chatId, out var existing))
        {
            if (existing.Connection.IsAlive)
            {
                return existing;
            }

            await RemoveSessionAsync(chatId);
        }

        var workingDir = GetWorkingDirectory(chatId);
        Directory.CreateDirectory(workingDir);

        var connection = new AcpConnection(_logger, _command, _args, workingDir, _environment);
        try
        {
            connection.Start();
            await connection.InitializeAsync(cancellationToken);
            var sessionId = await connection.NewSessionAsync(workingDir, cancellationToken);

            var session = new ChatSession(connection, sessionId);
            _sessions[chatId] = session;
            _logger.LogInformation("Created agy-acp session {SessionId} for chat {ChatId}", sessionId, chatId);
            return session;
        }
        catch
        {
            await connection.DisposeAsync();
            throw;
        }
    }

    private async Task RemoveSessionAsync(long chatId)
    {
        if (_sessions.TryRemove(chatId, out var session))
        {
            await session.Connection.DisposeAsync();
            _logger.LogInformation("Disposed agy-acp session for chat {ChatId}", chatId);
        }
    }

    private static Dictionary<string, string?> BuildEnvironment(PacosOptions options)
    {
        // CliWrap layers these on top of the inherited environment (null removes a
        // variable). Strip the bot's own configuration so secrets such as the
        // Telegram token can never reach the spawned agent, then inject only what
        // agy legitimately needs.
        var environment = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

        foreach (DictionaryEntry entry in Environment.GetEnvironmentVariables())
        {
            if (entry.Key is string key && key.StartsWith("Pacos__", StringComparison.OrdinalIgnoreCase))
            {
                environment[key] = null;
            }
        }

        if (!string.IsNullOrWhiteSpace(options.GeminiApiKey))
        {
            environment["GEMINI_API_KEY"] = options.GeminiApiKey;
        }

        // agy >= 1.1.x caps headless runs with its own --print-timeout (default
        // 5m), so align it with the bot's prompt timeout or longer turns would be
        // cut short by the CLI default. Injected first so a user-supplied
        // --print-timeout in AgyExtraArgs wins (later flags override earlier ones).
        var extraArgs = string.Create(CultureInfo.InvariantCulture, $"--print-timeout {options.PromptTimeoutSeconds}s");
        if (!string.IsNullOrWhiteSpace(options.AgyExtraArgs))
        {
            extraArgs = $"{extraArgs} {options.AgyExtraArgs}";
        }

        environment["AGY_EXTRA_ARGS"] = extraArgs;

        return environment;
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var chatId in _sessions.Keys.ToArray())
        {
            await RemoveSessionAsync(chatId);
        }

        foreach (var semaphore in _locks.Values)
        {
            semaphore.Dispose();
        }

        _locks.Clear();
    }

    private sealed record ChatSession(AcpConnection Connection, string SessionId);
}
