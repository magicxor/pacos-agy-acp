using Pacos.Services;
using Telegram.Bot.Types;

namespace Pacos.Tests.Unit;

internal sealed class RecordingUpdateDispatcher : ITelegramUpdateDispatcher
{
    public List<Update> Updates { get; } = [];

    public Task EnqueueUpdateAsync(Update update, CancellationToken cancellationToken)
    {
        Updates.Add(update);
        return Task.CompletedTask;
    }
}
