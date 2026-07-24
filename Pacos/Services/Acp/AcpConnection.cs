using System.Collections.Concurrent;
using System.IO.Pipelines;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using CliWrap;

namespace Pacos.Services.Acp;

/// <summary>
/// A single long-lived <c>agy-acp</c> subprocess driven over line-delimited
/// JSON-RPC via stdin/stdout. One connection corresponds to exactly one ACP
/// session (and therefore one agy conversation). The process is kept alive for
/// the lifetime of the connection and handles one prompt at a time.
/// </summary>
public sealed class AcpConnection : IAsyncDisposable
{
    private const string ClientName = "pacos";
    private const string ClientVersion = "0.1.0";

    private static readonly TimeSpan HandshakeTimeout = TimeSpan.FromSeconds(120);

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly ILogger _logger;
    private readonly string _command;
    private readonly IReadOnlyList<string> _args;
    private readonly string _workingDir;
    private readonly IReadOnlyDictionary<string, string?> _environment;

    private readonly Pipe _stdinPipe = new();
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private readonly ConcurrentDictionary<long, TaskCompletionSource<JsonElement>> _pending = new();
    private readonly StringBuilder _notificationBuffer = new();
    private readonly Lock _bufferGate = new();
    private readonly CancellationTokenSource _forcefulCts = new();

    private Task? _runTask;
    private long _nextId;
    private volatile bool _alive;

    public AcpConnection(
        ILogger logger,
        string command,
        IReadOnlyList<string> args,
        string workingDir,
        IReadOnlyDictionary<string, string?> environment)
    {
        _logger = logger;
        _command = command;
        _args = args;
        _workingDir = workingDir;
        _environment = environment;
    }

    /// <summary>
    /// True while the underlying process is running and able to serve requests.
    /// </summary>
    public bool IsAlive => _alive;

    /// <summary>
    /// Spawns the process and starts the background stdout reader. Must be called
    /// exactly once before any request method.
    /// </summary>
    public void Start()
    {
        var command = Cli.Wrap(_command)
            .WithArguments(_args)
            .WithWorkingDirectory(_workingDir)
            .WithEnvironmentVariables(_environment)
            .WithValidation(CommandResultValidation.None)
            .WithStandardInputPipe(PipeSource.FromStream(_stdinPipe.Reader.AsStream()))
            .WithStandardOutputPipe(PipeTarget.ToDelegate(OnStdoutLine))
            .WithStandardErrorPipe(PipeTarget.ToDelegate(OnStderrLine));

        _alive = true;
        _runTask = RunAsync(command);
        _logger.LogInformation("Spawned agy-acp process '{Command}' in '{WorkingDir}'", _command, _workingDir);
    }

    /// <summary>
    /// Performs the ACP <c>initialize</c> handshake.
    /// </summary>
    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        var parameters = new
        {
            protocolVersion = 1,
            clientCapabilities = new { },
            clientInfo = new { name = ClientName, version = ClientVersion },
        };

        await SendRequestAsync("initialize", parameters, HandshakeTimeout, cancellationToken);
    }

    /// <summary>
    /// Creates a new ACP session and returns its identifier.
    /// </summary>
    public async Task<string> NewSessionAsync(string cwd, CancellationToken cancellationToken)
    {
        var result = await SendRequestAsync(
            "session/new",
            new { cwd, mcpServers = Array.Empty<object>() },
            HandshakeTimeout,
            cancellationToken);

        if (result.ValueKind == JsonValueKind.Object
            && result.TryGetProperty("sessionId", out var sessionId)
            && sessionId.ValueKind == JsonValueKind.String
            && sessionId.GetString() is { Length: > 0 } sessionIdValue)
        {
            return sessionIdValue;
        }

        throw new AcpException("session/new did not return a sessionId");
    }

    /// <summary>
    /// Sets a session configuration option, e.g. the model the session runs on.
    /// agy-acp stores the value in its session state and passes it to every
    /// subsequent <c>agy</c> invocation (for <c>model</c> as <c>--model</c>).
    /// </summary>
    public async Task SetConfigOptionAsync(
        string sessionId,
        string configId,
        string value,
        CancellationToken cancellationToken)
    {
        await SendRequestAsync(
            "session/setConfigOption",
            new { sessionId, configId, value },
            HandshakeTimeout,
            cancellationToken);
    }

    /// <summary>
    /// Sends a single user prompt and returns the agent's full text response.
    /// </summary>
    public async Task<string> PromptAsync(
        string sessionId,
        string promptText,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        lock (_bufferGate)
        {
            _notificationBuffer.Clear();
        }

        var parameters = new
        {
            sessionId,
            prompt = new[] { new { type = "text", text = promptText } },
        };

        await SendRequestAsync("session/prompt", parameters, timeout, cancellationToken);

        lock (_bufferGate)
        {
            return _notificationBuffer.ToString();
        }
    }

    private async Task<JsonElement> SendRequestAsync(
        string method,
        object? parameters,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        if (!_alive)
        {
            throw new AcpException("agy-acp connection is not alive");
        }

        var id = Interlocked.Increment(ref _nextId);
        var tcs = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending[id] = tcs;

        try
        {
            var request = new JsonRpcRequest
            {
                Id = id,
                Method = method,
                Params = parameters,
            };

            await SendLineAsync(request, cancellationToken);

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(timeout);

            await using (timeoutCts.Token.Register(() => tcs.TrySetCanceled()))
            {
                try
                {
                    return await tcs.Task.ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    throw new TimeoutException($"agy-acp '{method}' request timed out after {timeout.TotalSeconds:0}s");
                }
            }
        }
        finally
        {
            _pending.TryRemove(id, out _);
        }
    }

    private async Task SendLineAsync(JsonRpcRequest request, CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(request, JsonOptions);
        var bytes = Encoding.UTF8.GetBytes(json + "\n");

        await _writeLock.WaitAsync(cancellationToken);
        try
        {
            await _stdinPipe.Writer.WriteAsync(bytes, cancellationToken);
            await _stdinPipe.Writer.FlushAsync(cancellationToken);
        }
        finally
        {
            _writeLock.Release();
        }

        _logger.LogTrace("acp_send {Json}", json);
    }

    private async Task RunAsync(Command command)
    {
        try
        {
            await command.ExecuteAsync(_forcefulCts.Token);
        }
        catch (OperationCanceledException)
        {
            // Expected when the connection is disposed.
        }
        catch (Exception e)
        {
            _logger.LogError(e, "agy-acp process terminated unexpectedly");
        }
        finally
        {
            _alive = false;
            FailAllPending(new AcpException("agy-acp process exited"));
        }
    }

    private void OnStdoutLine(string line)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            return;
        }

        _logger.LogTrace("acp_recv {Line}", line);

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(line);
        }
        catch (JsonException e)
        {
            _logger.LogWarning(e, "Failed to parse agy-acp output line: {Line}", line);
            return;
        }

        using (document)
        {
            var root = document.RootElement;

            // Notifications carry a method but no result/error. agy-acp only ever
            // sends session/update notifications and never calls back to the client.
            if (root.TryGetProperty("method", out var methodElement)
                && methodElement.ValueKind == JsonValueKind.String)
            {
                if (methodElement.GetString() == "session/update")
                {
                    HandleSessionUpdate(root);
                }

                return;
            }

            // Responses are correlated to a pending request by id.
            if (root.TryGetProperty("id", out var idElement) && idElement.TryGetInt64(out var id)
                && _pending.TryRemove(id, out var tcs))
            {
                CompletePending(tcs, root);
            }
        }
    }

    private static void CompletePending(TaskCompletionSource<JsonElement> tcs, JsonElement root)
    {
        if (root.TryGetProperty("error", out var errorElement) && errorElement.ValueKind == JsonValueKind.Object)
        {
            var code = errorElement.TryGetProperty("code", out var codeElement) && codeElement.TryGetInt64(out var c)
                ? c
                : 0;
            var message = errorElement.TryGetProperty("message", out var messageElement)
                ? messageElement.GetString()
                : "unknown error";
            tcs.TrySetException(new AcpException($"agy-acp error {code}: {message}"));
            return;
        }

        if (root.TryGetProperty("result", out var resultElement))
        {
            tcs.TrySetResult(resultElement.Clone());
            return;
        }

        tcs.TrySetResult(default);
    }

    private void HandleSessionUpdate(JsonElement root)
    {
        if (!root.TryGetProperty("params", out var parameters)
            || !parameters.TryGetProperty("update", out var update)
            || !update.TryGetProperty("sessionUpdate", out var sessionUpdate)
            || sessionUpdate.GetString() != "agent_message_chunk"
            || !update.TryGetProperty("content", out var content)
            || !content.TryGetProperty("text", out var textElement)
            || textElement.ValueKind != JsonValueKind.String)
        {
            return;
        }

        var text = textElement.GetString();
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        lock (_bufferGate)
        {
            _notificationBuffer.Append(text);
        }
    }

    private void OnStderrLine(string line)
    {
        if (!string.IsNullOrWhiteSpace(line))
        {
            _logger.LogWarning("agy-acp stderr: {Line}", line.TrimEnd());
        }
    }

    private void FailAllPending(Exception exception)
    {
        foreach (var id in _pending.Keys)
        {
            if (_pending.TryRemove(id, out var tcs))
            {
                tcs.TrySetException(exception);
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        _alive = false;

        try
        {
            await _stdinPipe.Writer.CompleteAsync();
        }
        catch (Exception e)
        {
            _logger.LogDebug(e, "Failed to complete agy-acp stdin pipe");
        }

        try
        {
            await _forcefulCts.CancelAsync();
        }
        catch (Exception e)
        {
            _logger.LogDebug(e, "Failed to cancel agy-acp process");
        }

        if (_runTask is not null)
        {
            try
            {
                await _runTask;
                _runTask.Dispose();
            }
            catch (Exception e)
            {
                _logger.LogDebug(e, "agy-acp run task faulted during disposal");
            }
        }

        FailAllPending(new AcpException("agy-acp connection disposed"));

        _forcefulCts.Dispose();
        _writeLock.Dispose();
    }

    private sealed class JsonRpcRequest
    {
        [JsonPropertyName("jsonrpc")]
        public string JsonRpc => "2.0";

        [JsonPropertyName("id")]
        public long Id { get; init; }

        [JsonPropertyName("method")]
        public string Method { get; init; } = string.Empty;

        [JsonPropertyName("params")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public object? Params { get; init; }
    }
}
