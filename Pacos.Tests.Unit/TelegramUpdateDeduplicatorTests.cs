using Pacos.Enums;
using Pacos.Services;

namespace Pacos.Tests.Unit;

[TestFixture]
[Parallelizable(scope: ParallelScope.All)]
internal sealed class TelegramUpdateDeduplicatorTests
{
    [Test]
    public void Reserve_NewId_IsAcquired()
    {
        Assert.That(new TelegramUpdateDeduplicator().Reserve(42), Is.EqualTo(UpdateReservation.Acquired));
    }

    [Test]
    public void Reserve_IdBeingQueued_IsInProgress()
    {
        var deduplicator = new TelegramUpdateDeduplicator();
        deduplicator.Reserve(42);

        Assert.That(deduplicator.Reserve(42), Is.EqualTo(UpdateReservation.InProgress));
    }

    [Test]
    public void Reserve_QueuedId_IsAlreadyQueued()
    {
        var deduplicator = new TelegramUpdateDeduplicator();
        deduplicator.Reserve(42);
        deduplicator.MarkQueued(42);

        Assert.That(deduplicator.Reserve(42), Is.EqualTo(UpdateReservation.AlreadyQueued));
    }

    [Test]
    public void Reserve_ReleasedId_IsAcquiredAgain()
    {
        var deduplicator = new TelegramUpdateDeduplicator();
        deduplicator.Reserve(42);
        deduplicator.Release(42);

        Assert.That(deduplicator.Reserve(42), Is.EqualTo(UpdateReservation.Acquired));
    }

    [Test]
    public void MarkQueued_BeyondCapacity_ForgetsOnlyTheOldestIds()
    {
        var deduplicator = new TelegramUpdateDeduplicator();
        for (var id = 0; id <= TelegramUpdateDeduplicator.Capacity; id++)
        {
            Queue(deduplicator, id);
        }

        Assert.Multiple(() =>
        {
            Assert.That(deduplicator.Reserve(TelegramUpdateDeduplicator.Capacity), Is.EqualTo(UpdateReservation.AlreadyQueued));
            Assert.That(deduplicator.Reserve(1), Is.EqualTo(UpdateReservation.AlreadyQueued));
            Assert.That(deduplicator.Reserve(0), Is.EqualTo(UpdateReservation.Acquired));
        });
    }

    /// <summary>
    /// A released reservation must leave nothing behind that could later evict a newer one.
    /// </summary>
    [Test]
    public void MarkQueued_AfterAnEarlierReleaseOfTheSameId_KeepsTheIdTracked()
    {
        var deduplicator = new TelegramUpdateDeduplicator();
        deduplicator.Reserve(42);
        deduplicator.Release(42);
        for (var id = 1000; id < 1000 + TelegramUpdateDeduplicator.Capacity - 1; id++)
        {
            Queue(deduplicator, id);
        }

        Queue(deduplicator, 42);

        Assert.That(deduplicator.Reserve(42), Is.EqualTo(UpdateReservation.AlreadyQueued));
    }

    private static void Queue(TelegramUpdateDeduplicator deduplicator, int updateId)
    {
        deduplicator.Reserve(updateId);
        deduplicator.MarkQueued(updateId);
    }
}
