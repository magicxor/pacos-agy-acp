using Pacos.Services;

namespace Pacos.Extensions;

public static class TelegramWebhookEndpointRouteBuilderExtensions
{
    /// <summary>
    /// Serves the Telegram webhook on the path of <paramref name="webhookUrl"/>, which the
    /// reverse proxy forwards to the container unchanged.
    /// </summary>
    public static IEndpointConventionBuilder MapTelegramWebhook(this IEndpointRouteBuilder endpoints, Uri webhookUrl)
        => endpoints.MapPost(
            webhookUrl.AbsolutePath,
            static (HttpRequest request, TelegramWebhookHandler handler, CancellationToken cancellationToken)
                => handler.HandleAsync(request, cancellationToken));
}
