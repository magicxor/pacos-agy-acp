using Pacos.Services;
using Polly;

namespace Pacos.Tests.Unit;

[TestFixture]
[Parallelizable(scope: ParallelScope.All)]
internal sealed class TelegramRequestTimeoutsTests
{
    private const string SendPhotoUrl = "https://api.telegram.org/bot0:token/sendPhoto";

    [Test]
    public void GetAttemptTimeout_MultipartRequest_ReturnsUploadAttempt()
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, SendPhotoUrl);
        request.Content = new MultipartFormDataContent();

        var timeout = GetAttemptTimeout(request);

        Assert.That(timeout, Is.EqualTo(TelegramRequestTimeouts.UploadAttempt));
    }

    [Test]
    public void GetAttemptTimeout_JsonRequest_ReturnsDefaultAttempt()
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, SendPhotoUrl);
        request.Content = new StringContent("{}");

        var timeout = GetAttemptTimeout(request);

        Assert.That(timeout, Is.EqualTo(TelegramRequestTimeouts.DefaultAttempt));
    }

    [Test]
    public void GetAttemptTimeout_NoRequestMessage_ReturnsDefaultAttempt()
    {
        var timeout = GetAttemptTimeout(request: null);

        Assert.That(timeout, Is.EqualTo(TelegramRequestTimeouts.DefaultAttempt));
    }

    private static TimeSpan GetAttemptTimeout(HttpRequestMessage? request)
    {
        var context = ResilienceContextPool.Shared.Get();
        try
        {
            context.SetRequestMessage(request);
            return TelegramRequestTimeouts.GetAttemptTimeout(context);
        }
        finally
        {
            ResilienceContextPool.Shared.Return(context);
        }
    }
}
