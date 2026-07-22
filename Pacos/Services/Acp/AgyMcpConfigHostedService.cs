using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Pacos.Models;
using Pacos.Models.Options;

namespace Pacos.Services.Acp;

/// <summary>
/// Publishes the MCP servers configured in <see cref="PacosOptions.McpServers"/> to agy by
/// (over)writing <c>$HOME/.gemini/antigravity-cli/mcp_config.json</c> on every startup,
/// mirroring how <see cref="AgySecurityPolicyHostedService"/> owns settings.json. The ACP
/// route (session/new mcpServers) is a dead end: the agy-acp adapter ignores request params
/// entirely, so the config file is the only channel agy actually reads.
///
/// Like the security policy, the file is owned by code: a stale or hand-edited config on the
/// state volume is overwritten, and a write failure stops the application (the same broken
/// volume would also break the security policy write, which is fail-closed by design).
/// The agent itself can never touch this file — mcp_config.json is on the security policy's
/// denied-paths list.
/// </summary>
public sealed class AgyMcpConfigHostedService : IHostedService
{
    private static readonly JsonSerializerOptions ConfigJsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        // agy expects lowercase transport names ("stdio", "sse").
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
        // Keep URLs with '&' human-readable instead of \uXXXX escapes.
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private readonly ILogger<AgyMcpConfigHostedService> _logger;
    private readonly Dictionary<string, McpServer> _mcpServers;

    public AgyMcpConfigHostedService(
        ILogger<AgyMcpConfigHostedService> logger,
        IOptions<PacosOptions> options)
    {
        _logger = logger;
        _mcpServers = options.Value.McpServers;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        // Resolve the same HOME that agy uses to locate its config.
        var home = Environment.GetEnvironmentVariable("HOME");
        if (string.IsNullOrWhiteSpace(home))
        {
            home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        }

        if (string.IsNullOrWhiteSpace(home))
        {
            throw new InvalidOperationException(
                "Cannot determine HOME directory to write the agy MCP config; refusing to start.");
        }

        var directory = Path.Combine(home, ".gemini", "antigravity-cli");
        var configPath = Path.Combine(directory, "mcp_config.json");

        try
        {
            Directory.CreateDirectory(directory);

            var config = new McpRoot { McpServers = _mcpServers };
            await File.WriteAllTextAsync(configPath, JsonSerializer.Serialize(config, ConfigJsonOptions), cancellationToken);

            _logger.LogInformation(
                "Wrote agy MCP config at {ConfigPath} ({ServerCount} server(s): {ServerNames})",
                configPath,
                _mcpServers.Count,
                string.Join(", ", _mcpServers.Keys));
        }
        catch (Exception e)
        {
            _logger.LogCritical(e, "Failed to write agy MCP config at {ConfigPath}; refusing to start", configPath);
            throw;
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
