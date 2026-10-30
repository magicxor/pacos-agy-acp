using Telegram.Bot.Types;

namespace Pacos.Services;

public interface ITelegramUpdateDispatcher
{
    Task EnqueueUpdateAsync(Update update, CancellationToken cancellationToken);
}
