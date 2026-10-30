using Pacos.Services;
using Telegram.Bot.Types;

namespace Pacos.Tests.Unit;

internal sealed class RecordingUpdateDispatcher : ITelegramUpdateDispatcher
{
    public List<Update> Updates { get; } = [];

    /// <summary>
    /// One-shot: the next call returns this task instead of recording its update, e.g. a
    /// cancelled one for a request aborted while the queue is full, or a pending one for a write
    /// still waiting for queue space.
    /// </summary>
    public Task? NextCallOutcome { get; set; }

    public Task EnqueueUpdateAsync(Update update, CancellationToken cancellationToken)
    {
        if (NextCallOutcome is { } outcome)
        {
            NextCallOutcome = null;
            return outcome;
        }

        Updates.Add(update);
        return Task.CompletedTask;
    }
}
