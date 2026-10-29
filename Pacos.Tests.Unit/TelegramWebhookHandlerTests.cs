using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Logging.Abstractions;
using Pacos.Services;

namespace Pacos.Tests.Unit;

[TestFixture]
[Parallelizable(scope: ParallelScope.All)]
internal sealed class TelegramWebhookHandlerTests
{
    private const string UpdateJson = """
        {"update_id":42,"message":{"message_id":7,"date":1700000000,"chat":{"id":-100111,"type":"supergroup"},"text":"pacos, hi"}}
        """;

    [Test]
    public async Task HandleAsync_ValidSecretAndUpdate_EnqueuesUpdateAndReturnsOk()
    {
        var secret = new TelegramWebhookSecret();
        var dispatcher = new RecordingUpdateDispatcher();

        var result = await HandleAsync(secret, dispatcher, secret.Value, UpdateJson);

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.InstanceOf<Ok>());
            Assert.That(dispatcher.Updates, Has.Count.EqualTo(1));
            Assert.That(dispatcher.Updates[0].Id, Is.EqualTo(42));
            Assert.That(dispatcher.Updates[0].Message?.Text, Is.EqualTo("pacos, hi"));
        });
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("wrong")]
    public async Task HandleAsync_MissingOrWrongSecret_ReturnsUnauthorizedWithoutEnqueueing(string? secretHeader)
    {
        var dispatcher = new RecordingUpdateDispatcher();

        var result = await HandleAsync(new TelegramWebhookSecret(), dispatcher, secretHeader, UpdateJson);

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.InstanceOf<UnauthorizedHttpResult>());
            Assert.That(dispatcher.Updates, Is.Empty);
        });
    }

    [TestCase("{not json")]
    [TestCase("null")]
    public async Task HandleAsync_MalformedBody_ReturnsBadRequestWithoutEnqueueing(string body)
    {
        var secret = new TelegramWebhookSecret();
        var dispatcher = new RecordingUpdateDispatcher();

        var result = await HandleAsync(secret, dispatcher, secret.Value, body);

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.InstanceOf<BadRequest>());
            Assert.That(dispatcher.Updates, Is.Empty);
        });
    }

    private static async Task<IResult> HandleAsync(
        TelegramWebhookSecret secret,
        RecordingUpdateDispatcher dispatcher,
        string? secretHeader,
        string body)
    {
        var handler = new TelegramWebhookHandler(NullLogger<TelegramWebhookHandler>.Instance, secret, dispatcher);

        await using var bodyStream = new MemoryStream(Encoding.UTF8.GetBytes(body));
        var context = new DefaultHttpContext();
        context.Request.Body = bodyStream;
        if (secretHeader is not null)
        {
            context.Request.Headers[TelegramWebhookHandler.SecretTokenHeader] = secretHeader;
        }

        return await handler.HandleAsync(context.Request, CancellationToken.None);
    }
}
