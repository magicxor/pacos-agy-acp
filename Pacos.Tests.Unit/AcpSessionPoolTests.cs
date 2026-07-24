using Pacos.Services.Acp;

namespace Pacos.Tests.Unit;

[TestFixture]
internal sealed class AcpSessionPoolTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 24, 12, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan IdleTimeout = TimeSpan.FromHours(3);

    [Test]
    public void IsIdleTimeoutExpired_ReturnsFalse_WhenNoPreviousActivity()
    {
        Assert.That(AcpSessionPool.IsIdleTimeoutExpired(null, Now, IdleTimeout), Is.False);
    }

    [Test]
    public void IsIdleTimeoutExpired_ReturnsFalse_WhenActivityIsWithinTimeout()
    {
        var lastActivityAt = Now.AddHours(-1);

        Assert.That(AcpSessionPool.IsIdleTimeoutExpired(lastActivityAt, Now, IdleTimeout), Is.False);
    }

    [Test]
    public void IsIdleTimeoutExpired_ReturnsFalse_WhenActivityIsExactlyAtTimeout()
    {
        var lastActivityAt = Now - IdleTimeout;

        Assert.That(AcpSessionPool.IsIdleTimeoutExpired(lastActivityAt, Now, IdleTimeout), Is.False);
    }

    [Test]
    public void IsIdleTimeoutExpired_ReturnsTrue_WhenActivityIsOlderThanTimeout()
    {
        var lastActivityAt = Now - IdleTimeout - TimeSpan.FromSeconds(1);

        Assert.That(AcpSessionPool.IsIdleTimeoutExpired(lastActivityAt, Now, IdleTimeout), Is.True);
    }

    [Test]
    public void IsIdleTimeoutExpired_ReturnsFalse_WhenTimeoutIsDisabled()
    {
        var lastActivityAt = Now.AddDays(-30);

        Assert.That(AcpSessionPool.IsIdleTimeoutExpired(lastActivityAt, Now, TimeSpan.Zero), Is.False);
    }
}
