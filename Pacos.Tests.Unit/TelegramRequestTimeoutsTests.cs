using Pacos.Services;
using Polly;
using Telegram.Bot;

namespace Pacos.Tests.Unit;

[TestFixture]
[Parallelizable(scope: ParallelScope.All)]
internal sealed class TelegramRequestTimeoutsTests
{
    private const string SendPhotoUrl = "https://api.telegram.org/bot0:token/sendPhoto";
    private const string GetFileUrl = "https://api.telegram.org/bot0:token/getFile";

    private static readonly (TimeSpan Attempt, TimeSpan Total) ReadTimeouts = (TelegramRequestTimeouts.ReadAttempt, TelegramRequestTimeouts.ReadTotal);
    private static readonly (TimeSpan Attempt, TimeSpan Total) DefaultTimeouts = (TelegramRequestTimeouts.DefaultAttempt, TelegramRequestTimeouts.DefaultTotal);
    private static readonly (TimeSpan Attempt, TimeSpan Total) UploadTimeouts = (TelegramRequestTimeouts.UploadAttempt, TelegramRequestTimeouts.UploadTotal);

    [Test]
    public void GetTimeouts_MultipartRequest_ReturnsUploadTimeouts()
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, SendPhotoUrl);
        request.Content = new MultipartFormDataContent();

        Assert.That(GetTimeouts(request), Is.EqualTo(UploadTimeouts));
    }

    [Test]
    public void GetTimeouts_JsonSendRequest_ReturnsDefaultTimeouts()
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, SendPhotoUrl);
        request.Content = new StringContent("{}");

        Assert.That(GetTimeouts(request), Is.EqualTo(DefaultTimeouts));
    }

    [Test]
    public void GetTimeouts_JsonGetRequest_ReturnsReadTimeouts()
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, GetFileUrl);
        request.Content = new StringContent("{}");

        Assert.That(GetTimeouts(request), Is.EqualTo(ReadTimeouts));
    }

    [Test]
    public void GetTimeouts_NoRequestMessage_ReturnsDefaultTimeouts()
    {
        Assert.That(GetTimeouts(request: null), Is.EqualTo(DefaultTimeouts));
    }

    [Test]
    public async Task GetTimeouts_GetFileSentByTelegramBotClient_ReturnsReadTimeouts()
    {
        using var handler = new RecordingHttpMessageHandler("""{"ok":true,"result":{"file_id":"file","file_unique_id":"unique"}}""");
        using var httpClient = new HttpClient(handler);
        var botClient = new TelegramBotClient("123:token", httpClient);

        await botClient.GetFile("file");

        Assert.That(GetTimeouts(handler.Requests.Single()), Is.EqualTo(ReadTimeouts));
    }

    private static (TimeSpan Attempt, TimeSpan Total) GetTimeouts(HttpRequestMessage? request)
    {
        var context = ResilienceContextPool.Shared.Get();
        try
        {
            context.SetRequestMessage(request);
            return (TelegramRequestTimeouts.GetAttemptTimeout(context), TelegramRequestTimeouts.GetTotalTimeout(context));
        }
        finally
        {
            ResilienceContextPool.Shared.Return(context);
        }
    }
}
