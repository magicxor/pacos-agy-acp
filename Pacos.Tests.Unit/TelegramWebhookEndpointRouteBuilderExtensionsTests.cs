using System.Net;
using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Pacos.Extensions;
using Pacos.Services;

namespace Pacos.Tests.Unit;

/// <summary>
/// Runs the webhook route on a real Kestrel instance, so the minimal-API parameter binding
/// (the handler resolved from DI, not parsed from the body) is exercised end to end.
/// </summary>
[TestFixture]
[Parallelizable(scope: ParallelScope.All)]
internal sealed class TelegramWebhookEndpointRouteBuilderExtensionsTests
{
    private const string UpdateJson = """
        {"update_id":42,"message":{"message_id":7,"date":1700000000,"chat":{"id":-100111,"type":"supergroup"},"text":"pacos, hi"}}
        """;

    private static readonly Uri WebhookUrl = new("https://pacos.example.com/telegram/webhook");

    [Test]
    public async Task MapTelegramWebhook_PostToWebhookPathWithSecret_EnqueuesUpdate()
    {
        var secret = new TelegramWebhookSecret();
        var dispatcher = new RecordingUpdateDispatcher();
        await using var app = await StartAppAsync(secret, dispatcher);

        var statusCode = await PostAsync(app, "/telegram/webhook", secret.Value);

        Assert.Multiple(() =>
        {
            Assert.That(statusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(dispatcher.Updates, Has.Count.EqualTo(1));
            Assert.That(dispatcher.Updates[0].Id, Is.EqualTo(42));
        });
    }

    [Test]
    public async Task MapTelegramWebhook_PostWithoutSecret_ReturnsUnauthorized()
    {
        var dispatcher = new RecordingUpdateDispatcher();
        await using var app = await StartAppAsync(new TelegramWebhookSecret(), dispatcher);

        var statusCode = await PostAsync(app, "/telegram/webhook", secretToken: null);

        Assert.Multiple(() =>
        {
            Assert.That(statusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
            Assert.That(dispatcher.Updates, Is.Empty);
        });
    }

    [Test]
    public async Task MapTelegramWebhook_PostToOtherPath_ReturnsNotFound()
    {
        var secret = new TelegramWebhookSecret();
        var dispatcher = new RecordingUpdateDispatcher();
        await using var app = await StartAppAsync(secret, dispatcher);

        var statusCode = await PostAsync(app, "/telegram/other", secret.Value);

        Assert.Multiple(() =>
        {
            Assert.That(statusCode, Is.EqualTo(HttpStatusCode.NotFound));
            Assert.That(dispatcher.Updates, Is.Empty);
        });
    }

    private static async Task<WebApplication> StartAppAsync(TelegramWebhookSecret secret, RecordingUpdateDispatcher dispatcher)
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Logging.ClearProviders();
        builder.Services.AddSingleton(secret);
        builder.Services.AddSingleton<ITelegramUpdateDispatcher>(dispatcher);
        builder.Services.AddSingleton<TelegramUpdateDeduplicator>();
        builder.Services.AddSingleton<TelegramWebhookHandler>();

        var app = builder.Build();
        app.MapTelegramWebhook(WebhookUrl);
        await app.StartAsync();
        return app;
    }

    private static async Task<HttpStatusCode> PostAsync(WebApplication app, string path, string? secretToken)
    {
        using var client = new HttpClient();
        client.BaseAddress = new Uri(app.Urls.Single());
        using var content = new StringContent(UpdateJson, Encoding.UTF8, "application/json");
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(path, UriKind.Relative));
        request.Content = content;
        if (secretToken is not null)
        {
            request.Headers.Add(TelegramWebhookHandler.SecretTokenHeader, secretToken);
        }

        using var response = await client.SendAsync(request);
        return response.StatusCode;
    }
}
