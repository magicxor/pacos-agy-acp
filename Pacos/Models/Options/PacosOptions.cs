using System.ComponentModel.DataAnnotations;

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
    /// </summary>
    [Range(1, 3600)]
    public int PromptTimeoutSeconds { get; set; } = 300;

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
