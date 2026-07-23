using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Options;
using NLog;
using NLog.Config;
using NLog.Extensions.Logging;
using NTextCat;
using Pacos.Enums;
using Pacos.Models.Options;
using Pacos.Services;
using Pacos.Services.Acp;
using Pacos.Services.BackgroundTasks;
using Pacos.Services.ChatCommandHandlers;
using Pacos.Services.GenerativeAi;
using Pacos.Services.ImageConversion;
using Pacos.Services.Markdown;
using Pacos.Services.VideoConversion;
using Telegram.Bot;
using LogLevel = Microsoft.Extensions.Logging.LogLevel;

#pragma warning disable S6667

namespace Pacos;

public sealed class Program
{
    private const string NLogConfigFileName = "nlog.config";
    private const string RankedLanguageIdentifierFileName = "Core14.profile.xml";
    private const int BackgroundTaskQueueCapacity = 100;

    private static readonly LoggingConfiguration LoggingConfiguration = new XmlLoggingConfiguration(NLogConfigFileName);

    public static void Main(string[] args)
    {
        // NLog: set up the logger first to catch all errors
        LogManager.Configuration = LoggingConfiguration;
        try
        {
            var host = Host.CreateDefaultBuilder(args)
                .ConfigureAppConfiguration((_, configBuilder) => configBuilder.AddEnvironmentVariables())
                .ConfigureLogging(loggingBuilder =>
                {
                    loggingBuilder.ClearProviders();
                    loggingBuilder.SetMinimumLevel(LogLevel.Trace);
                    loggingBuilder.AddNLog(LoggingConfiguration);
                })
                .ConfigureServices((hostContext, services) =>
                {
                    services
                        .AddOptions<PacosOptions>()
                        .Bind(hostContext.Configuration.GetSection(nameof(OptionSections.Pacos)))
                        .ValidateDataAnnotations()
                        .ValidateOnStart();

                    // Registered first so its StartAsync runs (and the agy security
                    // policy is enforced) before any other hosted service.
                    services.AddHostedService<AgySecurityPolicyHostedService>();

                    // Publishes PacosOptions.McpServers to agy (mcp_config.json); the
                    // security policy above only allows MCP tools for these servers.
                    services.AddHostedService<AgyMcpConfigHostedService>();

                    var telegramRequestTimeout = TimeSpan.FromSeconds(40);
                    services.AddHttpClient(nameof(HttpClientType.Telegram), httpClient => httpClient.Timeout = Timeout.InfiniteTimeSpan)
                        .AddDefaultLogger()
                        .AddStandardResilienceHandler(x =>
                        {
                            x.AttemptTimeout = new HttpTimeoutStrategyOptions { Timeout = telegramRequestTimeout };
                            x.TotalRequestTimeout = new HttpTimeoutStrategyOptions { Timeout = x.AttemptTimeout.Timeout * 2 };
                            x.CircuitBreaker.SamplingDuration = x.AttemptTimeout.Timeout * 2;
                        });

                    services.AddSingleton<VideoConverter>();
                    services.AddSingleton<ImageDownscaler>();
                    services.AddSingleton<TimeProvider>(_ => TimeProvider.System);
                    services.AddSingleton<ITelegramBotClient>(s => new TelegramBotClient(
                            s.GetRequiredService<IOptions<PacosOptions>>().Value.TelegramBotApiKey,
                            s.GetRequiredService<IHttpClientFactory>().CreateClient(nameof(HttpClientType.Telegram))
                        ));
                    services.AddHostedService<QueuedHostedService>();
                    services.AddSingleton<MarkdownConversionService>();
                    services.AddSingleton<IBackgroundTaskQueue>(_ => new BackgroundTaskQueue(BackgroundTaskQueueCapacity));
                    services.AddSingleton<RankedLanguageIdentifier>(_ => new RankedLanguageIdentifierFactory().Load(RankedLanguageIdentifierFileName));
                    services.AddSingleton<AcpSessionPool>();
                    services.AddSingleton<ChatService>();
                    services.AddSingleton<TelegramMediaService>();
                    services.AddSingleton<OutputFileSender>();
                    services.AddSingleton<DrawHandler>();
                    services.AddSingleton<ResetHandler>();
                    services.AddSingleton<MentionHandler>();
                    services.AddSingleton<TelegramBotService>();
                    services.AddHostedService<Worker>();
                })
                .Build();

            host.Run();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // NLog: catch setup errors
            LogManager.GetCurrentClassLogger().Error(ex, "Stopped program because of exception");
            throw;
        }
        catch (OperationCanceledException)
        {
            // This is expected when the application is shutting down gracefully
            LogManager.GetCurrentClassLogger().Info("Application shut down gracefully.");
        }
        finally
        {
            // Ensure to flush and stop internal timers/threads before application-exit (Avoid segmentation fault on Linux)
            LogManager.Shutdown();
        }
    }
}
