using Telegram.Bot;
using Telegram.Bot.Args;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Requests;
using Telegram.Bot.Requests.Abstractions;
using Telegram.Bot.Types;

namespace Pacos.Tests.Unit;

/// <summary>
/// Answers getUpdates calls from a script, one step per call (a step may throw to simulate a
/// failed poll). Once the script runs out it cancels polling, the way a host shutdown would.
/// deleteWebhook calls succeed unless <see cref="DeleteWebhookScript"/> holds a step for them.
/// </summary>
internal sealed class ScriptedTelegramBotClient : ITelegramBotClient
{
    private readonly CancellationTokenSource _stopPolling;
    private readonly Queue<Func<Update[]>> _getUpdatesScript;

    public ScriptedTelegramBotClient(CancellationTokenSource stopPolling, params Func<Update[]>[] getUpdatesScript)
    {
        _stopPolling = stopPolling;
        _getUpdatesScript = new Queue<Func<Update[]>>(getUpdatesScript);
    }

    public event AsyncEventHandler<ApiRequestEventArgs>? OnMakingApiRequest
    {
        add { /* Never raised: the fake makes no HTTP calls. */ }
        remove { /* Never raised: the fake makes no HTTP calls. */ }
    }

    public event AsyncEventHandler<ApiResponseEventArgs>? OnApiResponseReceived
    {
        add { /* Never raised: the fake makes no HTTP calls. */ }
        remove { /* Never raised: the fake makes no HTTP calls. */ }
    }

    public List<object> Requests { get; } = [];

    public List<GetUpdatesRequest> GetUpdatesRequests => [.. Requests.OfType<GetUpdatesRequest>()];

    public Queue<Func<bool>> DeleteWebhookScript { get; } = new();

    public bool LocalBotServer => false;

    public long BotId => 1;

    public TimeSpan Timeout { get; set; }

    public IExceptionParser ExceptionsParser { get; set; } = new DefaultExceptionParser();

    public async Task<TResponse> SendRequest<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default)
    {
        if (request is not (GetUpdatesRequest or DeleteWebhookRequest))
        {
            throw new NotSupportedException($"Unexpected request {request.MethodName}");
        }

        Requests.Add(request);

        if (request is DeleteWebhookRequest)
        {
            return (TResponse)(object)(!DeleteWebhookScript.TryDequeue(out var deleteStep) || deleteStep());
        }

        if (_getUpdatesScript.TryDequeue(out var step))
        {
            return (TResponse)(object)step();
        }

        await _stopPolling.CancelAsync();
        throw new OperationCanceledException(cancellationToken);
    }

    public Task<bool> TestApi(CancellationToken cancellationToken = default)
        => throw new NotSupportedException();

    public Task DownloadFile(string filePath, Stream destination, CancellationToken cancellationToken = default)
        => throw new NotSupportedException();

    public Task DownloadFile(TGFile file, Stream destination, CancellationToken cancellationToken = default)
        => throw new NotSupportedException();
}
