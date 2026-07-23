using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Pacos.Constants;
using Pacos.Models;
using Pacos.Models.Options;

namespace Pacos.Services.Acp;

/// <summary>
/// Publishes the MCP servers configured in <see cref="PacosOptions.McpServers"/> to agy by
/// (over)writing <c>$HOME/.gemini/config/mcp_config.json</c> on every startup, mirroring how
/// <see cref="AgySecurityPolicyHostedService"/> owns settings.json. This is the current agy
/// config location (verified against a live agy installation; <c>~/.gemini/antigravity-cli/
/// mcp_config.json</c> is the pre-migration legacy path). The ACP route (session/new
/// mcpServers) is a dead end: the agy-acp adapter ignores request params entirely, so the
/// config file is the only channel agy actually reads.
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
    private readonly PacosOptions _options;

    public AgyMcpConfigHostedService(
        ILogger<AgyMcpConfigHostedService> logger,
        IOptions<PacosOptions> options)
    {
        _logger = logger;
        _options = options.Value;
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

        var directory = Path.Combine(home, ".gemini", "config");
        var configPath = Path.Combine(directory, "mcp_config.json");

        try
        {
            Directory.CreateDirectory(directory);

            var workspaceRoot = AcpSessionPool.ResolveRoot(_options);
            var brainDirectory = $"{home.Replace('\\', '/').TrimEnd('/')}/.gemini/antigravity-cli/brain";
            await File.WriteAllTextAsync(
                configPath,
                BuildConfigJson(_options.McpServers, workspaceRoot, brainDirectory, _options.Crawl4AiApiToken ?? string.Empty),
                cancellationToken);

            _logger.LogInformation(
                "Wrote agy MCP config at {ConfigPath} ({ServerCount} server(s): {ServerNames})",
                configPath,
                _options.McpServers.Count,
                string.Join(", ", _options.McpServers.Keys));
        }
        catch (Exception e)
        {
            _logger.LogCritical(e, "Failed to write agy MCP config at {ConfigPath}; refusing to start", configPath);
            throw;
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <summary>
    /// Renders the mcp_config.json content, substituting the workspace-root, brain-dir and
    /// crawl4ai-token placeholders in env values: <see cref="Const.WorkspaceRootPlaceholder"/> and
    /// <see cref="Const.Crawl4AiApiTokenPlaceholder"/> raw (a literal path prefix and a bearer
    /// token respectively) and <see cref="Const.WorkspaceRootPatternPlaceholder"/> /
    /// <see cref="Const.BrainDirPlaceholder"/> regex-escaped (for FileMove regex patterns). The
    /// source dictionary is never mutated — it is the live options singleton. Kept separate (and
    /// public) so tests can pin the exact JSON shape agy expects — stdio entries must come out as
    /// bare command/args/env.
    /// </summary>
    public static string BuildConfigJson(Dictionary<string, McpServer> mcpServers, string workspaceRoot, string brainDirectory, string crawl4aiApiToken)
    {
        // Paths inlined into FileMove regex patterns must be regex-escaped: the brain path
        // contains '.', and WorkingDirectoryRoot is user-configurable and may contain other
        // metacharacters. The raw workspace root stays available for gallerydl, which consumes
        // it as a literal path prefix (escaping would corrupt that).
        var workspaceRootPattern = Regex.Escape(workspaceRoot);
        var brainPattern = Regex.Escape(brainDirectory);

        var servers = mcpServers.ToDictionary(
            pair => pair.Key,
            pair => SubstitutePlaceholders(pair.Value, workspaceRoot, workspaceRootPattern, brainPattern, crawl4aiApiToken));

        return JsonSerializer.Serialize(new McpRoot { McpServers = servers }, ConfigJsonOptions);
    }

    private static McpServer SubstitutePlaceholders(McpServer server, string workspaceRoot, string workspaceRootPattern, string brainPattern, string crawl4aiApiToken)
    {
        if (server.Env is not { Count: > 0 } env)
        {
            return server;
        }

        return new McpServer
        {
            Type = server.Type,
            Name = server.Name,
            Command = server.Command,
            Args = server.Args,
            // Substitute {workspaceRootPattern} before {workspaceRoot} so the shorter raw
            // placeholder can never clip the longer escaped one.
            Env = env.ToDictionary(
                pair => pair.Key,
                pair => pair.Value
                    ?.Replace(Const.WorkspaceRootPatternPlaceholder, workspaceRootPattern, StringComparison.Ordinal)
                    .Replace(Const.WorkspaceRootPlaceholder, workspaceRoot, StringComparison.Ordinal)
                    .Replace(Const.BrainDirPlaceholder, brainPattern, StringComparison.Ordinal)
                    .Replace(Const.Crawl4AiApiTokenPlaceholder, crawl4aiApiToken, StringComparison.Ordinal)),
            EnvFile = server.EnvFile,
            Url = server.Url,
            Headers = server.Headers,
        };
    }
}
