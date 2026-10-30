using Pacos.Services;

namespace Pacos.Tests.Unit;

[TestFixture]
[Parallelizable(scope: ParallelScope.All)]
internal sealed class TelegramUpdateDeduplicatorTests
{
    [Test]
    public void TryReserve_NewId_ReturnsTrue()
    {
        Assert.That(new TelegramUpdateDeduplicator().TryReserve(42), Is.True);
    }

    [Test]
    public void TryReserve_ReservedId_ReturnsFalse()
    {
        var deduplicator = new TelegramUpdateDeduplicator();
        deduplicator.TryReserve(42);

        Assert.That(deduplicator.TryReserve(42), Is.False);
    }

    [Test]
    public void TryReserve_ReleasedId_ReturnsTrue()
    {
        var deduplicator = new TelegramUpdateDeduplicator();
        deduplicator.TryReserve(42);
        deduplicator.Release(42);

        Assert.That(deduplicator.TryReserve(42), Is.True);
    }

    [Test]
    public void TryReserve_BeyondCapacity_ForgetsOnlyTheOldestIds()
    {
        var deduplicator = new TelegramUpdateDeduplicator();
        for (var id = 0; id <= TelegramUpdateDeduplicator.Capacity; id++)
        {
            deduplicator.TryReserve(id);
        }

        Assert.Multiple(() =>
        {
            Assert.That(deduplicator.TryReserve(TelegramUpdateDeduplicator.Capacity), Is.False);
            Assert.That(deduplicator.TryReserve(1), Is.False);
            Assert.That(deduplicator.TryReserve(0), Is.True);
        });
    }
}
