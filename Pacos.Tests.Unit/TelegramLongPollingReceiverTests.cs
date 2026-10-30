using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Pacos.Services;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Requests;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

namespace Pacos.Tests.Unit;

[TestFixture]
[Parallelizable(scope: ParallelScope.All)]
internal sealed class TelegramLongPollingReceiverTests
{
    private static readonly UpdateType[] AllowedUpdates = [UpdateType.Message];
    private static readonly int[] FirstBatchIds = [10, 11];
    private static readonly int?[] OffsetsAfterFirstBatch = [null, 12, 12];
    private static readonly TimeSpan PollTimeout = TimeSpan.FromSeconds(10);
    private static readonly Type[] DeleteRetriedThenPolled = [typeof(DeleteWebhookRequest), typeof(DeleteWebhookRequest), typeof(GetUpdatesRequest)];

    [Test]
    public void ReceiveAsync_DispatchesUpdatesInOrderAndConfirmsThemWithTheNextOffset()
    {
        using var stopPolling = new CancellationTokenSource();
        var client = new ScriptedTelegramBotClient(stopPolling, () => [CreateUpdate(10), CreateUpdate(11)], () => []);
        var dispatcher = new RecordingUpdateDispatcher();

        Assert.CatchAsync<OperationCanceledException>(
            () => CreateReceiver(client, new FakeTimeProvider()).ReceiveAsync(dispatcher, AllowedUpdates, stopPolling.Token));

        Assert.Multiple(() =>
        {
            Assert.That(dispatcher.Updates.Select(static u => u.Id), Is.EqualTo(FirstBatchIds));
            Assert.That(client.GetUpdatesRequests.Select(static r => r.Offset), Is.EqualTo(OffsetsAfterFirstBatch));
        });
    }

    [Test]
    public void ReceiveAsync_EveryPoll_IsALongPollWithinTheAttemptTimeout()
    {
        using var stopPolling = new CancellationTokenSource();
        var client = new ScriptedTelegramBotClient(stopPolling, () => [], () => []);

        Assert.CatchAsync<OperationCanceledException>(
            () => CreateReceiver(client, new FakeTimeProvider()).ReceiveAsync(new RecordingUpdateDispatcher(), AllowedUpdates, stopPolling.Token));

        Assert.Multiple(() =>
        {
            Assert.That(client.GetUpdatesRequests, Has.Count.EqualTo(3));
            Assert.That(
                client.GetUpdatesRequests.Select(static r => r.Timeout),
                Has.All.EqualTo((int)TelegramRequestTimeouts.LongPolling.TotalSeconds));
            Assert.That(client.GetUpdatesRequests.Select(static r => r.AllowedUpdates), Has.All.EqualTo(AllowedUpdates));
            Assert.That(TelegramRequestTimeouts.LongPolling, Is.GreaterThan(TimeSpan.Zero).And.LessThan(TelegramRequestTimeouts.ReadAttempt));
        });
    }

    [Test]
    public void ReceiveAsync_AfterFailedPoll_PausesBeforePollingAgain()
    {
        using var stopPolling = new CancellationTokenSource();
        var timeProvider = new FakeTimeProvider();
        var client = new ScriptedTelegramBotClient(
            stopPolling,
            () => throw new RequestException("Bot API Service Failure"),
            () => [CreateUpdate(10)]);
        var dispatcher = new RecordingUpdateDispatcher();

        var receiving = CreateReceiver(client, timeProvider).ReceiveAsync(dispatcher, AllowedUpdates, stopPolling.Token);
        timeProvider.Advance(TelegramLongPollingReceiver.ErrorDelay - TimeSpan.FromSeconds(1));
        var pollsDuringPause = client.GetUpdatesRequests.Count;
        timeProvider.Advance(TimeSpan.FromSeconds(1));

        Assert.CatchAsync<OperationCanceledException>(() => receiving.WaitAsync(PollTimeout));
        Assert.Multiple(() =>
        {
            Assert.That(pollsDuringPause, Is.EqualTo(1));
            Assert.That(client.GetUpdatesRequests, Has.Count.EqualTo(3));
            Assert.That(dispatcher.Updates.Select(static u => u.Id), Has.Exactly(1).EqualTo(10));
        });
    }

    [Test]
    public void ReceiveAsync_WhenFloodControlAsksForALongerPause_WaitsThatLong()
    {
        using var stopPolling = new CancellationTokenSource();
        var timeProvider = new FakeTimeProvider();
        var client = new ScriptedTelegramBotClient(
            stopPolling,
            () => throw new ApiRequestException("Too Many Requests", 429, new ResponseParameters { RetryAfter = 30, }),
            () => []);

        var receiving = CreateReceiver(client, timeProvider).ReceiveAsync(new RecordingUpdateDispatcher(), AllowedUpdates, stopPolling.Token);
        timeProvider.Advance(TimeSpan.FromSeconds(29));
        var pollsDuringPause = client.GetUpdatesRequests.Count;
        timeProvider.Advance(TimeSpan.FromSeconds(1));

        Assert.CatchAsync<OperationCanceledException>(() => receiving.WaitAsync(PollTimeout));
        Assert.Multiple(() =>
        {
            Assert.That(pollsDuringPause, Is.EqualTo(1));
            Assert.That(client.GetUpdatesRequests, Has.Count.EqualTo(3));
        });
    }

    [Test]
    public void ReceiveAsync_DeletesTheWebhookOnceBeforeTheFirstPoll()
    {
        using var stopPolling = new CancellationTokenSource();
        var client = new ScriptedTelegramBotClient(stopPolling, () => [], () => []);

        Assert.CatchAsync<OperationCanceledException>(
            () => CreateReceiver(client, new FakeTimeProvider()).ReceiveAsync(new RecordingUpdateDispatcher(), AllowedUpdates, stopPolling.Token));

        Assert.Multiple(() =>
        {
            Assert.That(client.Requests[0], Is.InstanceOf<DeleteWebhookRequest>());
            Assert.That(client.Requests.OfType<DeleteWebhookRequest>().Count(), Is.EqualTo(1));
            Assert.That(client.GetUpdatesRequests, Has.Count.EqualTo(3));
        });
    }

    /// <summary>
    /// Polling next to a webhook that is still registered would only ever get 409 Conflict, so a
    /// failed deletion must be retried rather than skipped.
    /// </summary>
    [Test]
    public void ReceiveAsync_WhenWebhookDeletionFails_RetriesItBeforePolling()
    {
        using var stopPolling = new CancellationTokenSource();
        var timeProvider = new FakeTimeProvider();
        var client = new ScriptedTelegramBotClient(stopPolling);
        client.DeleteWebhookScript.Enqueue(() => throw new RequestException("Bot API Service Failure"));

        var receiving = CreateReceiver(client, timeProvider).ReceiveAsync(new RecordingUpdateDispatcher(), AllowedUpdates, stopPolling.Token);
        var requestsDuringPause = client.Requests.Count;
        timeProvider.Advance(TelegramLongPollingReceiver.ErrorDelay);

        Assert.CatchAsync<OperationCanceledException>(() => receiving.WaitAsync(PollTimeout));
        Assert.Multiple(() =>
        {
            Assert.That(requestsDuringPause, Is.EqualTo(1));
            Assert.That(client.Requests.Select(static r => r.GetType()), Is.EqualTo(DeleteRetriedThenPolled));
        });
    }

    private static TelegramLongPollingReceiver CreateReceiver(ScriptedTelegramBotClient client, FakeTimeProvider timeProvider)
        => new(NullLogger<TelegramLongPollingReceiver>.Instance, client, timeProvider);

    private static Update CreateUpdate(int id) => new() { Id = id, };
}
