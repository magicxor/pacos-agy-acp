using Pacos.Models.Options;
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

    [Test]
    public void BuildEnvironment_InjectsTheChatId()
    {
        var environment = AcpSessionPool.BuildEnvironment(CreateOptions(), -1001234567890, []);

        Assert.That(
            environment[AcpSessionPool.TelegramChatIdEnvironmentVariable],
            Is.EqualTo("-1001234567890"));
    }

    [Test]
    public void BuildEnvironment_InjectsADistinctChatIdPerChat()
    {
        var options = CreateOptions();

        var first = AcpSessionPool.BuildEnvironment(options, -100111, []);
        var second = AcpSessionPool.BuildEnvironment(options, -100222, []);

        Assert.Multiple(() =>
        {
            Assert.That(first[AcpSessionPool.TelegramChatIdEnvironmentVariable], Is.EqualTo("-100111"));
            Assert.That(second[AcpSessionPool.TelegramChatIdEnvironmentVariable], Is.EqualTo("-100222"));
        });
    }

    /// <summary>
    /// The chat id must never be stripped along with the bot's own configuration: MCP servers
    /// spawned by agy inherit it and have no other way to tell which chat they are serving.
    /// </summary>
    [Test]
    public void BuildEnvironment_StripsBotConfigurationButKeepsTheChatId()
    {
        string[] inheritedVariableNames =
        [
            "Pacos__TelegramBotApiKey",
            "PACOS__ALLOWEDCHATIDS__0",
            AcpSessionPool.TelegramChatIdEnvironmentVariable,
            "PATH",
        ];

        var environment = AcpSessionPool.BuildEnvironment(CreateOptions(), -100333, inheritedVariableNames);

        Assert.Multiple(() =>
        {
            Assert.That(environment["Pacos__TelegramBotApiKey"], Is.Null);
            Assert.That(environment["PACOS__ALLOWEDCHATIDS__0"], Is.Null);
            Assert.That(environment[AcpSessionPool.TelegramChatIdEnvironmentVariable], Is.EqualTo("-100333"));
            Assert.That(environment.ContainsKey("PATH"), Is.False);
        });
    }

    private static PacosOptions CreateOptions() => new()
    {
        TelegramBotApiKey = "123456:test-token",
        AllowedChatIds = [-100111],
        ChatModel = "Gemini 3.5 Flash (High)",
    };
}
