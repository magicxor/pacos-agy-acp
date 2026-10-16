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
}
