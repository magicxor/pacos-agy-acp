using System.ComponentModel.DataAnnotations;
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

    private static List<ValidationResult> Validate(string? agyExtraArgs)
    {
        var options = new PacosOptions
        {
            TelegramBotApiKey = "123456:test-token",
            AllowedChatIds = [-100111],
            ChatModel = "Gemini 3.5 Flash (High)",
            AgyExtraArgs = agyExtraArgs,
        };

        var results = new List<ValidationResult>();
        Validator.TryValidateObject(options, new ValidationContext(options), results, validateAllProperties: true);
        return results;
    }
}
