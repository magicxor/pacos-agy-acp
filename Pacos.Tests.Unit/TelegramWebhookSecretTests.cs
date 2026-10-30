using Pacos.Services;

namespace Pacos.Tests.Unit;

[TestFixture]
[Parallelizable(scope: ParallelScope.All)]
internal sealed class TelegramWebhookSecretTests
{
    [Test]
    public void Value_UsesOnlyCharactersTelegramAccepts()
    {
        var secret = new TelegramWebhookSecret();

        Assert.That(secret.Value, Does.Match("^[A-Za-z0-9_-]{1,256}$"));
    }

    [Test]
    public void Value_DiffersBetweenInstances()
    {
        var first = new TelegramWebhookSecret();
        var second = new TelegramWebhookSecret();

        Assert.That(first.Value, Is.Not.EqualTo(second.Value));
    }

    [Test]
    public void Matches_OwnValue_ReturnsTrue()
    {
        var secret = new TelegramWebhookSecret();

        Assert.That(secret.Matches(secret.Value), Is.True);
    }

    [Test]
    public void Matches_AnotherInstanceValue_ReturnsFalse()
    {
        var secret = new TelegramWebhookSecret();

        Assert.That(secret.Matches(new TelegramWebhookSecret().Value), Is.False);
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("wrong")]
    public void Matches_MissingOrWrongValue_ReturnsFalse(string? candidate)
    {
        Assert.That(new TelegramWebhookSecret().Matches(candidate), Is.False);
    }
}
