using Pacos.Services;
using Telegram.Bot.Types;

namespace Pacos.Tests.Unit;

internal sealed class RecordingUpdateDispatcher : ITelegramUpdateDispatcher
{
    public List<Update> Updates { get; } = [];

    /// <summary>
    /// Makes the next call fail the way an aborted request or a shutdown does while the queue is full.
    /// </summary>
    public bool CancelNextCall { get; set; }

    public Task EnqueueUpdateAsync(Update update, CancellationToken cancellationToken)
    {
        if (CancelNextCall)
        {
            CancelNextCall = false;
            return Task.FromCanceled(new CancellationToken(canceled: true));
        }

        Updates.Add(update);
        return Task.CompletedTask;
    }
}
