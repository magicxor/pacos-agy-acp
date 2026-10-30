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

    [Test]
    public async Task HandleAsync_RepeatedDelivery_ReturnsOkWithoutEnqueueingAgain()
    {
        var secret = new TelegramWebhookSecret();
        var dispatcher = new RecordingUpdateDispatcher();
        var handler = CreateHandler(secret, dispatcher);

        var first = await HandleAsync(handler, secret.Value, UpdateJson);
        var repeat = await HandleAsync(handler, secret.Value, UpdateJson);

        Assert.Multiple(() =>
        {
            Assert.That(first, Is.InstanceOf<Ok>());
            Assert.That(repeat, Is.InstanceOf<Ok>());
            Assert.That(dispatcher.Updates, Has.Count.EqualTo(1));
        });
    }

    [Test]
    public async Task HandleAsync_DeliveryCancelledBeforeQueueing_AcceptsTheRepeat()
    {
        var secret = new TelegramWebhookSecret();
        var dispatcher = new RecordingUpdateDispatcher { NextCallOutcome = Task.FromCanceled(new CancellationToken(canceled: true)), };
        var handler = CreateHandler(secret, dispatcher);

        Assert.CatchAsync<OperationCanceledException>(() => HandleAsync(handler, secret.Value, UpdateJson));
        var repeat = await HandleAsync(handler, secret.Value, UpdateJson);

        Assert.Multiple(() =>
        {
            Assert.That(repeat, Is.InstanceOf<Ok>());
            Assert.That(dispatcher.Updates, Has.Count.EqualTo(1));
        });
    }

    /// <summary>
    /// A repeat that arrives while the original delivery is still waiting for queue space must
    /// not be acknowledged: the original may yet be cancelled, and the update would be lost.
    /// </summary>
    [Test]
    public async Task HandleAsync_RepeatWhileTheOriginalIsStillQueueing_AsksTelegramToRetryLater()
    {
        var secret = new TelegramWebhookSecret();
        var pendingWrite = new TaskCompletionSource();
        var dispatcher = new RecordingUpdateDispatcher { NextCallOutcome = pendingWrite.Task, };
        var handler = CreateHandler(secret, dispatcher);

        var original = HandleAsync(handler, secret.Value, UpdateJson);
        var originalIsQueueing = SpinWait.SpinUntil(() => dispatcher.NextCallOutcome is null, TimeSpan.FromSeconds(10));
        var repeatWhileQueueing = await HandleAsync(handler, secret.Value, UpdateJson);
        pendingWrite.SetCanceled();
        Assert.CatchAsync<OperationCanceledException>(() => original);
        var repeatAfterCancellation = await HandleAsync(handler, secret.Value, UpdateJson);

        Assert.Multiple(() =>
        {
            Assert.That(originalIsQueueing, Is.True);
            Assert.That(
                repeatWhileQueueing,
                Is.InstanceOf<StatusCodeHttpResult>()
                    .With.Property(nameof(StatusCodeHttpResult.StatusCode)).EqualTo(StatusCodes.Status503ServiceUnavailable));
            Assert.That(repeatAfterCancellation, Is.InstanceOf<Ok>());
            Assert.That(dispatcher.Updates, Has.Count.EqualTo(1));
        });
    }

    private static TelegramWebhookHandler CreateHandler(TelegramWebhookSecret secret, RecordingUpdateDispatcher dispatcher)
        => new(NullLogger<TelegramWebhookHandler>.Instance, secret, dispatcher, new TelegramUpdateDeduplicator());

    private static Task<IResult> HandleAsync(
        TelegramWebhookSecret secret,
        RecordingUpdateDispatcher dispatcher,
        string? secretHeader,
        string body)
        => HandleAsync(CreateHandler(secret, dispatcher), secretHeader, body);

    private static async Task<IResult> HandleAsync(TelegramWebhookHandler handler, string? secretHeader, string body)
    {
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
