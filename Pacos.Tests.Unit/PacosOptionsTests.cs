using System.ComponentModel.DataAnnotations;
using Microsoft.Extensions.Configuration;
using Pacos.Models.Options;

namespace Pacos.Tests.Unit;

[TestFixture]
[Parallelizable(scope: ParallelScope.All)]
internal sealed class PacosOptionsTests
{
    [TestCase(null)]
    [TestCase("")]
    [TestCase("--model 'Gemini 3.5 Flash (High)'")]
    public void AgyExtraArgs_WithoutPrintTimeout_IsValid(string? agyExtraArgs)
    {
        Assert.That(Validate(agyExtraArgs), Is.Empty);
    }

    [TestCase("--print-timeout 60s")]
    [TestCase("--print-timeout=60s")]
    [TestCase("--model x\n--print-timeout 60s")]
    public void AgyExtraArgs_WithPrintTimeout_IsRejected(string agyExtraArgs)
    {
        var results = Validate(agyExtraArgs);

        Assert.That(
            results.SelectMany(static result => result.MemberNames),
            Does.Contain(nameof(PacosOptions.AgyExtraArgs)));
    }

    [TestCase(null)]
    [TestCase("https://pacos.example.com/telegram/webhook")]
    [TestCase("https://pacos.example.com:8443/telegram/webhook")]
    public void WebhookUrl_MissingOrAbsoluteHttps_IsValid(string? webhookUrl)
    {
        var options = CreateOptions();
        options.WebhookUrl = webhookUrl is null ? null : new Uri(webhookUrl);

        Assert.That(Validate(options), Is.Empty);
    }

    [TestCase("http://pacos.example.com/telegram/webhook")]
    [TestCase("/telegram/webhook")]
    [TestCase("pacos.example.com/telegram/webhook")]
    public void WebhookUrl_NotAbsoluteHttps_IsRejected(string webhookUrl)
    {
        var options = CreateOptions();
        options.WebhookUrl = new Uri(webhookUrl, UriKind.RelativeOrAbsolute);

        Assert.That(
            Validate(options).SelectMany(static result => result.MemberNames),
            Does.Contain(nameof(PacosOptions.WebhookUrl)));
    }

    /// <summary>
    /// The deploy compose file always passes <c>Pacos__WebhookUrl</c>; an environment without a
    /// webhook passes it empty, which must bind to no webhook rather than an invalid empty URI.
    /// </summary>
    [Test]
    public void WebhookUrl_BoundFromEmptyValue_IsNull()
    {
        var options = Bind(string.Empty);

        Assert.Multiple(() =>
        {
            Assert.That(options.WebhookUrl, Is.Null);
            Assert.That(Validate(options), Is.Empty);
        });
    }

    [Test]
    public void WebhookUrl_BoundFromUrl_IsAbsoluteUri()
    {
        var options = Bind("https://pacos.example.com/telegram/webhook");

        Assert.Multiple(() =>
        {
            Assert.That(options.WebhookUrl, Is.EqualTo(new Uri("https://pacos.example.com/telegram/webhook")));
            Assert.That(options.WebhookUrl?.AbsolutePath, Is.EqualTo("/telegram/webhook"));
        });
    }

    private static PacosOptions Bind(string webhookUrl)
    {
        var options = CreateOptions();
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { [nameof(PacosOptions.WebhookUrl)] = webhookUrl, })
            .Build()
            .Bind(options);
        return options;
    }

    private static PacosOptions CreateOptions() => new()
    {
        TelegramBotApiKey = "123456:test-token",
        AllowedChatIds = [-100111],
        ChatModel = "Gemini 3.5 Flash (High)",
    };

    private static List<ValidationResult> Validate(string? agyExtraArgs)
    {
        var options = CreateOptions();
        options.AgyExtraArgs = agyExtraArgs;
        return Validate(options);
    }

    private static List<ValidationResult> Validate(PacosOptions options)
    {
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(options, new ValidationContext(options), results, validateAllProperties: true);
        return results;
    }
}
