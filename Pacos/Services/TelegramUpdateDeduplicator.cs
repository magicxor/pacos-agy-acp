using Pacos.Enums;

namespace Pacos.Services;

/// <summary>
/// Tracks webhook updates by id so each is queued once. Telegram delivers webhooks at least
/// once: it repeats a delivery whenever it misses the 200 response, even if the update was
/// already queued, and handling the repeat would run the same command twice.
/// </summary>
public sealed class TelegramUpdateDeduplicator
{
    // Repeats follow the original within minutes, so a short history of queued ids is enough.
    internal const int Capacity = 1000;

    private readonly Lock _lock = new();
    private readonly HashSet<int> _inProgressIds = [];
    private readonly HashSet<int> _queuedIds = [];
    private readonly Queue<int> _queuedOrder = new();

    /// <summary>
    /// Reserves <paramref name="updateId"/> unless another delivery is queueing it or already has.
    /// After <see cref="UpdateReservation.Acquired"/> the caller must end with
    /// <see cref="MarkQueued"/> or <see cref="Release"/>.
    /// </summary>
    public UpdateReservation Reserve(int updateId)
    {
        lock (_lock)
        {
            if (_queuedIds.Contains(updateId))
            {
                return UpdateReservation.AlreadyQueued;
            }

            return _inProgressIds.Add(updateId) ? UpdateReservation.Acquired : UpdateReservation.InProgress;
        }
    }

    /// <summary>
    /// Records that the reserved update was queued, so its repeats are ignored from now on.
    /// </summary>
    public void MarkQueued(int updateId)
    {
        lock (_lock)
        {
            _inProgressIds.Remove(updateId);

            // An id enters the history and the eviction order together and leaves both on
            // eviction, so the order never holds a stale entry that could evict a newer one.
            if (!_queuedIds.Add(updateId))
            {
                return;
            }

            _queuedOrder.Enqueue(updateId);
            if (_queuedOrder.Count > Capacity)
            {
                _queuedIds.Remove(_queuedOrder.Dequeue());
            }
        }
    }

    /// <summary>
    /// Releases the reservation of an update that was not queued after all, so its repeat is accepted.
    /// </summary>
    public void Release(int updateId)
    {
        lock (_lock)
        {
            _inProgressIds.Remove(updateId);
        }
    }
}
