using System.ComponentModel.DataAnnotations;
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
    /// the agy working directory and holds its steering file (GEMINI.md) and
    /// per-turn temporary input/output folders. When empty, a folder under the
    /// system temp directory is used.
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
    /// Hard timeout (in seconds) for a single prompt round-trip to agy-acp.
    /// Also forwarded to agy as <c>--print-timeout</c> so the CLI's own headless
    /// timeout (default 5m) never undercuts this value.
    /// </summary>
    [Range(1, 3600)]
    public int PromptTimeoutSeconds { get; set; } = 300;

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
#pragma warning disable S5332 // plain http is intentional: container-to-container traffic on the internal compose network
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
                // The Dockerfile empties AllowedPathPrefixes in the server's appsettings.json
                // at image build time, so this single index fully defines the allow-list
                // (arrays merge per index across configuration providers).
                ["GalleryDlApi__AllowedPathPrefixes__0"] = Const.WorkspaceRootPlaceholder,
            },
        },
        ["filemcp"] = new McpServer
        {
            Command = "dotnet",
            Args = ["/opt/file-mcp/FileMcp.dll"],
            Env = new Dictionary<string, string?>
            {
                // The Dockerfile empties both allow-list arrays in the server's
                // appsettings.json at image build time, so these single index-0
                // overrides fully define the allow-list (arrays merge per index across
                // configuration providers). Confine both the source and the target to
                // the per-chat workspace subtree (which contains the per-turn output
                // dir), mirroring gallerydl's workspace-root scoping. The resolved root
                // is a plain /tmp path with no regex metacharacters, so it is safe to
                // inline into these anchored FileMove patterns.
                ["FileMove__AllowedSourcePatterns__0"] = $"^{Const.WorkspaceRootPlaceholder}(/.*)?$",
                ["FileMove__AllowedTargetPatterns__0"] = $"^{Const.WorkspaceRootPlaceholder}(/.*)?$",
                // Per-turn files are destroyed once the turn ends, so any file the agent
                // can legitimately move was created during the current turn (bounded by
                // PromptTimeoutSeconds, default 300s). Keep this a tight, turn-scoped
                // bound: comfortably above PromptTimeoutSeconds so a long turn's fresh
                // file is never falsely rejected, but far too short to let a future
                // path-traversal bug reach a stale file. Do NOT widen it toward hours/days
                // (there are no legitimately old files to move); if PromptTimeoutSeconds
                // is ever raised above this, bump this in step, not the other way around.
                ["FileMove__MaxFileAgeSeconds"] = "600",
            },
        },
    };
#pragma warning restore S5332

    /// <summary>
    /// Which set of agy <c>command(...)</c> permission rules to write into
    /// settings.json. Lets you A/B different hardening strategies at runtime
    /// without rebuilding. Accepted values (case-insensitive):
    /// <list type="bullet">
    /// <item><c>nolookahead</c> (default) — RE2-safe whitelist expressed as a set of
    /// fully-anchored deny patterns (no negative lookahead). Robust to agy's actual
    /// regex engine and matching semantics.</item>
    /// <item><c>lookahead</c> — compact whitelist using negative lookahead; only works
    /// if agy's engine is PCRE-style. Fails (blocks everything) on an RE2 engine.</item>
    /// <item><c>denyall</c> — block every shell command (<c>command(*)</c>).</item>
    /// <item><c>off</c> — no command rules at all (agy default-allows commands).</item>
    /// </list>
    /// </summary>
    public string AgyCommandRuleMode { get; set; } = "nolookahead";
}
