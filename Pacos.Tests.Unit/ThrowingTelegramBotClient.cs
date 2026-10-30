using Telegram.Bot;
using Telegram.Bot.Args;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Requests.Abstractions;
using Telegram.Bot.Types;

namespace Pacos.Tests.Unit;

/// <summary>
/// Fails every Bot API call with the exception produced by the given factory.
/// </summary>
internal sealed class ThrowingTelegramBotClient : ITelegramBotClient
{
    private readonly Func<Exception> _exceptionFactory;

    public ThrowingTelegramBotClient(Func<Exception> exceptionFactory)
    {
        _exceptionFactory = exceptionFactory;
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

    public bool LocalBotServer => false;

    public long BotId => 1;

    public TimeSpan Timeout { get; set; }

    public IExceptionParser ExceptionsParser { get; set; } = new DefaultExceptionParser();

    public Task<TResponse> SendRequest<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default)
        => Task.FromException<TResponse>(_exceptionFactory());

    public Task<bool> TestApi(CancellationToken cancellationToken = default)
        => Task.FromException<bool>(_exceptionFactory());

    public Task DownloadFile(string filePath, Stream destination, CancellationToken cancellationToken = default)
        => Task.FromException(_exceptionFactory());

    public Task DownloadFile(TGFile file, Stream destination, CancellationToken cancellationToken = default)
        => Task.FromException(_exceptionFactory());
}
