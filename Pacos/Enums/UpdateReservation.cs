namespace Pacos.Enums;

public enum UpdateReservation
{
    /// <summary>The caller now owns queueing the update.</summary>
    Acquired,

    /// <summary>Another delivery of the update is still being queued and may yet fail.</summary>
    InProgress,

    /// <summary>The update was already queued, so this delivery is a repeat.</summary>
    AlreadyQueued,
}
