namespace Pacos.Services;

/// <summary>
/// Remembers the ids of recently accepted webhook updates. Telegram delivers webhooks at least
/// once: it repeats a delivery whenever it misses the 200 response, even if the update was
/// already queued, and handling the repeat would run the same command twice.
/// </summary>
public sealed class TelegramUpdateDeduplicator
{
    // Repeats follow the original within minutes, so a short history of ids is enough.
    internal const int Capacity = 1000;

    private readonly Lock _lock = new();
    private readonly HashSet<int> _reservedIds = [];
    private readonly Queue<int> _reservationOrder = new();

    /// <summary>
    /// Reserves <paramref name="updateId"/>; returns <see langword="false"/> when it is already reserved.
    /// </summary>
    public bool TryReserve(int updateId)
    {
        lock (_lock)
        {
            if (!_reservedIds.Add(updateId))
            {
                return false;
            }

            _reservationOrder.Enqueue(updateId);
            if (_reservationOrder.Count > Capacity)
            {
                _reservedIds.Remove(_reservationOrder.Dequeue());
            }

            return true;
        }
    }

    /// <summary>
    /// Releases the reservation of an update that was not queued after all, so its repeat is accepted.
    /// </summary>
    public void Release(int updateId)
    {
        lock (_lock)
        {
            // The id stays in the eviction order; evicting that stale entry later only forgets
            // the id a little early, long after Telegram would repeat it.
            _reservedIds.Remove(updateId);
        }
    }
}
