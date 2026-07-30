using System.ComponentModel.DataAnnotations;
using System.Diagnostics.CodeAnalysis;
using Pacos.Constants;

namespace Pacos.Models.Options;

public sealed class PacosOptions
{
    [Required]
    [RegularExpression(".*:.*")]
    public required string TelegramBotApiKey { get; set; }

    [Required]
    [MinLength(1)]
    public required long[] AllowedChatIds { get; set; }

    /// <summary>
    /// Name of the model written into the agy permission policy
    /// (the <c>model</c> field of <c>settings.json</c>), e.g.
    /// <c>Gemini 3.5 Flash (High)</c>.
    /// </summary>
    [Required]
    [MinLength(1)]
    public required string ChatModel { get; set; }

    /// <summary>
    /// Optional model to retry a prompt on when the primary <see cref="ChatModel"/>
    /// fails with a quota/rate-limit error (HTTP 429 / RESOURCE_EXHAUSTED). Pick a
    /// model billed against a different quota pool than the primary one (e.g. a
    /// Claude or GPT model when the primary is Gemini); the value must exactly
    /// match a label from <c>agy models</c>, e.g. <c>Claude Sonnet 4.6 (Thinking)</c>.
    /// After a successful fallback turn the chat keeps its fallback session (and
    /// conversation context) until the session is next torn down — by an error, a
    /// reset command or a restart — after which the next session starts on the
    /// primary model again. Empty disables the fallback.
    /// </summary>
    public string? FallbackChatModel { get; set; }

    /// <summary>
    /// Executable used to spawn the agy-acp ACP adapter process.
    /// </summary>
    public string AgyAcpCommand { get; set; } = "agy-acp";

    /// <summary>
    /// Extra command-line arguments passed to the agy-acp process itself.
    /// </summary>
    public string[] AgyAcpArgs { get; set; } = [];

    /// <summary>
    /// Root directory under which per-chat working directories are created.
    /// Each chat gets its own subdirectory (named after the chat id) that becomes
    /// the agy working directory and holds its steering file (AGENTS.md), its agent
    /// skills (.agents/skills) and per-turn temporary input/output folders. When
    /// empty, a folder under the system temp directory is used.
    /// </summary>
    public string? WorkingDirectoryRoot { get; set; }

    /// <summary>
    /// Extra arguments forwarded to every underlying <c>agy</c> invocation via the
    /// <c>AGY_EXTRA_ARGS</c> environment variable (whitespace separated).
    /// </summary>
    public string? AgyExtraArgs { get; set; }

    /// <summary>
    /// Optional Gemini API key passed to the agy subprocess (as <c>GEMINI_API_KEY</c>)
    /// for non-interactive authentication. When empty, agy relies on its own
    /// persisted OAuth credentials (e.g. <c>~/.gemini</c>).
    /// </summary>
    public string? GeminiApiKey { get; set; }

    /// <summary>
    /// Bearer token for the crawl4ai REST backend (sent by the Crawl4AiMcp server as
    /// <c>Authorization: Bearer &lt;token&gt;</c>). Shared secret: the crawl4ai sidecar must be
    /// started with the same value in its <c>CRAWL4AI_API_TOKEN</c> environment variable. This is
    /// mandatory in practice — a token-less crawl4ai 0.9.x binds to loopback only and is
    /// unreachable from the pacos container (every call fails with "Connection refused"). The value
    /// is substituted into the crawl4ai MCP server env in place of
    /// <see cref="Const.Crawl4AiApiTokenPlaceholder"/> at startup by
    /// <see cref="Services.Acp.AgyMcpConfigHostedService"/>.
    /// </summary>
    public string? Crawl4AiApiToken { get; set; }

    /// <summary>
    /// Hard timeout (in seconds) for a single prompt round-trip to agy-acp.
    /// Also forwarded to agy as <c>--print-timeout</c> so the CLI's own headless
    /// timeout (default 5m) never undercuts this value.
    /// </summary>
    [Range(1, 3600)]
    public int PromptTimeoutSeconds { get; set; } = 300;

    /// <summary>
    /// When a chat has been idle (no prompt to the agent) for longer than this
    /// many minutes, the next prompt starts a fresh session — exactly as if the
    /// reset command had been issued — instead of resuming the old conversation.
    /// Resuming replays the whole conversation history to the model on every
    /// turn, so dropping context that has gone stale saves tokens. <c>0</c>
    /// disables the idle reset.
    /// </summary>
    [Range(0, 10080)]
    public int SessionIdleTimeoutMinutes { get; set; } = 180;

    /// <summary>
    /// MCP servers agy should load, keyed by server name. Written to
    /// <c>~/.gemini/config/mcp_config.json</c> on startup by
    /// <see cref="Services.Acp.AgyMcpConfigHostedService"/>; the security policy
    /// allows MCP tool calls only for the server names listed here (everything
    /// else is auto-denied by headless agy). Env values may contain
    /// <see cref="Const.WorkspaceRootPlaceholder"/>, which is replaced at startup
    /// with the resolved workspace root (<see cref="Services.Acp.AcpSessionPool.ResolveRoot"/>),
    /// so file-saving allow-lists always track <see cref="WorkingDirectoryRoot"/>.
    /// </summary>
    [SuppressMessage("Minor Vulnerability", "S5332:Clear-text protocols should not be used", Justification = "Plain http is intentional: container-to-container traffic on the internal compose network")]
    public Dictionary<string, McpServer> McpServers { get; set; } = new()
    {
        ["gallerydl"] = new McpServer
        {
            Command = "dotnet",
            Args = ["/opt/gallerydl-mcp/GalleryDl.McpServer.dll"],
            Env = new Dictionary<string, string?>
            {
                ["GalleryDlApi__BaseUrl"] = "http://gallerydl-webapi:8080",
                ["GalleryDlApi__MaxTake"] = "10",
                ["GalleryDlApi__AllowedPathPrefixes__0"] = Const.WorkspaceRootPlaceholder,
            },
        },
        ["filemcp"] = new McpServer
        {
            Command = "dotnet",
            Args = ["/opt/file-mcp/FileMcp.dll"],
            Env = new Dictionary<string, string?>
            {
                ["FileMove__AllowedSourcePatterns__0"] = $"^{Const.BrainDirPlaceholder}(/.*)?$",
                ["FileMove__AllowedSourcePatterns__1"] =
                    $"^{Const.WorkspaceRootPatternPlaceholder}/[^/]+/\\.turns/[^/]+/temp(/.*)?$",
                ["FileMove__AllowedTargetPatterns__0"] =
                    $"^{Const.WorkspaceRootPatternPlaceholder}/[^/]+/\\.turns/[^/]+/output(/.*)?$",
                ["FileMove__MaxFileAgeSeconds"] = "600",
            },
        },
        ["crawl4ai"] = new McpServer
        {
            Command = "dotnet",
            Args = ["/opt/crawl4ai-mcp/Crawl4AiMcp.dll"],
            Env = new Dictionary<string, string?>
            {
                ["Crawl4Ai__BaseUrl"] = "http://crawl4ai:11235",
                ["Crawl4Ai__ApiToken"] = Const.Crawl4AiApiTokenPlaceholder,
                ["Crawl4Ai__AllowedOutputPatterns__0"] =
                    $"^{Const.WorkspaceRootPatternPlaceholder}/[^/]+/\\.turns/[^/]+/(output|temp)(/.*)?$",
            },
        },
        ["quickchart"] = new McpServer
        {
            Command = "dotnet",
            Args = ["/opt/quickchart-mcp/QuickChartMcp.dll"],
            Env = new Dictionary<string, string?>
            {
                ["QuickChart__BaseUrl"] = "http://quickchart:3400",
                ["QuickChart__AllowedOutputPatterns__0"] =
                    $"^{Const.WorkspaceRootPatternPlaceholder}/[^/]+/\\.turns/[^/]+/(output|temp)(/.*)?$",
            },
        },
    };

    /// <summary>
    /// Which set of agy command-permission rules to write into settings.json.
    /// Accepted values (case-insensitive):
    /// <list type="bullet">
    /// <item><c>denyall</c> (default) — block every shell command by denying both the
    /// <c>command(*)</c> and <c>unsandboxed(*)</c> verbs. The agent has no legitimate
    /// use for the shell: file delivery goes through the filemcp MCP server.</item>
    /// <item><c>off</c> — no command rules at all (agy default-allows commands). For
    /// local debugging only.</item>
    /// </list>
    /// </summary>
    public string AgyCommandRuleMode { get; set; } = "denyall";
}
