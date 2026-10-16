using Pacos.Services.Acp;

namespace Pacos.Tests.Unit;

[TestFixture]
[Parallelizable(scope: ParallelScope.All)]
internal sealed class AgyTurnTimeoutsTests
{
    [Test]
    public void FromBudget_DefaultBudget_ProducesExpectedTiers()
    {
        var timeouts = AgyTurnTimeouts.FromBudget(300);

        Assert.Multiple(() =>
        {
            Assert.That(timeouts.Turn, Is.EqualTo(TimeSpan.FromSeconds(300)));
            Assert.That(timeouts.CliBackstop, Is.EqualTo(TimeSpan.FromSeconds(315)));
            Assert.That(timeouts.Watchdog, Is.EqualTo(TimeSpan.FromSeconds(345)));
        });
    }

    [TestCase(1)]
    [TestCase(7)]
    [TestCase(300)]
    [TestCase(3600)]
    public void FromBudget_AlwaysOrdersTiersStrictly(int turnSeconds)
    {
        var timeouts = AgyTurnTimeouts.FromBudget(turnSeconds);

        Assert.Multiple(() =>
        {
            Assert.That(timeouts.CliBackstop, Is.GreaterThanOrEqualTo(timeouts.Turn + TimeSpan.FromSeconds(1)));
            Assert.That(timeouts.Watchdog, Is.GreaterThanOrEqualTo(timeouts.CliBackstop + TimeSpan.FromSeconds(1)));
        });
    }

    /// <summary>
    /// After the turn deadline agy-acp still kills agy and drains pipes a descendant may hold
    /// (up to its 5s PIPE_DRAIN_TIMEOUT), so even the shortest budget must leave the full
    /// cleanup allowance before the watchdog gives up on the reply.
    /// </summary>
    [TestCase(1)]
    [TestCase(7)]
    [TestCase(60)]
    [TestCase(300)]
    public void FromBudget_AlwaysLeavesTheAdapterCleanupAllowanceAfterTheTurn(int turnSeconds)
    {
        var timeouts = AgyTurnTimeouts.FromBudget(turnSeconds);

        Assert.Multiple(() =>
        {
            Assert.That(timeouts.Watchdog - timeouts.Turn, Is.GreaterThanOrEqualTo(AgyTurnTimeouts.AdapterCleanupAllowance));
            Assert.That(AgyTurnTimeouts.AdapterCleanupAllowance, Is.GreaterThan(TimeSpan.FromSeconds(5)));
        });
    }

    [Test]
    public void FromBudget_ShortBudget_WatchdogCoversTheCleanup()
    {
        Assert.That(AgyTurnTimeouts.FromBudget(1).Watchdog, Is.EqualTo(TimeSpan.FromSeconds(11)));
    }
}
