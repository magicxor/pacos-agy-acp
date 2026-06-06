using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Pacos.Models.Options;

namespace Pacos.Services.Acp;

/// <summary>
/// Enforces the agy Fine-Grained Permissions policy by (over)writing
/// <c>$HOME/.gemini/antigravity-cli/settings.json</c> on every startup, before
/// the bot begins accepting messages (and therefore before any <c>agy</c>
/// process is spawned).
///
/// IMPORTANT: agy's permission engine is NOT a hard security boundary in headless
/// (<c>agy -p</c>) mode — unconfigured actions default to <em>allow</em> (fail-open).
/// Its exact regex flavor and anchoring are undocumented/observed to be inconsistent,
/// so the default command policy uses RE2-safe, fully-anchored deny patterns WITHOUT
/// negative lookahead (see the "nolookahead" mode). These rules are best-effort
/// defense-in-depth ONLY. The real isolation boundary must be the container
/// (read-only rootfs, dropped capabilities, pids/memory/cpu limits, no-new-privileges,
/// egress allow-list).
///
/// The policy is the single source of truth and lives in code so it cannot be
/// forgotten or silently replaced by a mounted volume / stale image layer. If
/// the file cannot be written the application fails to start (fail-closed) so
/// the agent never runs with weaker-than-intended permissions.
/// </summary>
public sealed class AgySecurityPolicyHostedService : IHostedService
{
    private static readonly JsonSerializerOptions PolicyJsonOptions = new()
    {
        WriteIndented = true,
        // Keep regex characters such as '+', '<', '>', '&' literal instead of
        // emitting \uXXXX escapes, so the generated policy is human-readable.
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private readonly ILogger<AgySecurityPolicyHostedService> _logger;
    private readonly string _workspaceRoot;
    private readonly string _commandRuleMode;

    public AgySecurityPolicyHostedService(
        ILogger<AgySecurityPolicyHostedService> logger,
        IOptions<PacosOptions> options)
    {
        _logger = logger;
        _workspaceRoot = NormalizePath(AcpSessionPool.ResolveRoot(options.Value));
        _commandRuleMode = (options.Value.AgyCommandRuleMode ?? "nolookahead").Trim().ToLowerInvariant();
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        // Resolve the same HOME that agy uses to locate its settings.
        var home = Environment.GetEnvironmentVariable("HOME");
        if (string.IsNullOrWhiteSpace(home))
        {
            home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        }

        if (string.IsNullOrWhiteSpace(home))
        {
            throw new InvalidOperationException(
                "Cannot determine HOME directory to write the agy security policy; refusing to start.");
        }

        var normalizedHome = NormalizePath(home);
        var directory = Path.Combine(home, ".gemini", "antigravity-cli");
        var settingsPath = Path.Combine(directory, "settings.json");

        try
        {
            Directory.CreateDirectory(directory);
            Directory.CreateDirectory(_workspaceRoot);

            await File.WriteAllTextAsync(settingsPath, BuildSettingsJson(normalizedHome), cancellationToken);

            _logger.LogInformation(
                "Enforced agy security policy at {SettingsPath} (command rule mode: {Mode})",
                settingsPath,
                _commandRuleMode);
        }
        catch (Exception e)
        {
            _logger.LogCritical(e, "Failed to enforce agy security policy at {SettingsPath}; refusing to start", settingsPath);
            throw;
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <summary>
    /// agy normalizes paths with forward slashes; keep generated paths valid on
    /// Windows too by converting backslashes and trimming a trailing slash.
    /// </summary>
    private static string NormalizePath(string path) => path.Replace('\\', '/').TrimEnd('/');

    /// <summary>
    /// Builds the agy permission policy. Precedence is strictly Deny &gt; Ask &gt; Allow,
    /// so an allow rule cannot carve an exception out of a broader deny — that is
    /// why parents of paths the agent legitimately needs are never denied, and
    /// every sensitive sibling is instead denied by name.
    /// </summary>
    private string BuildSettingsJson(string home)
    {
        var gemini = $"{home}/.gemini";
        var cli = $"{gemini}/antigravity-cli";

        List<string> allow = ["read_url(*)", "execute_url(*)"];
        List<string> deny = ["mcp(*)"];

        // Grants/denies both read_file and write_file for a path in one call —
        // the policy intent is that anything we let the agent write it may also
        // read, and anything we forbid writing it must not read either.
        void AllowReadWrite(string path)
        {
            allow.Add($"read_file({path})");
            allow.Add($"write_file({path})");
        }

        void DenyReadWrite(string path)
        {
            deny.Add($"read_file({path})");
            deny.Add($"write_file({path})");
        }

        // The per-chat workspace root and the agy brain staging dir are the only
        // file locations the agent may touch.
        AllowReadWrite(_workspaceRoot);
        AllowReadWrite($"{cli}/brain");

        // Every sensitive path the agent must never read or write. The exact same
        // list also drives the cp command deny rules below, so the file policy and
        // the command policy can never drift apart (see CollectDeniedPaths).
        var deniedPaths = CollectDeniedPaths(home, gemini, cli);
        foreach (var path in deniedPaths)
        {
            DenyReadWrite(path);
        }

        AppendCommandRules(allow, deny, deniedPaths);

        var policy = new { model = "Gemini 3.5 Flash (High)", permissions = new { allow, deny }, toolPermission = "strict" };
        return JsonSerializer.Serialize(policy, PolicyJsonOptions);
    }

    /// <summary>
    /// The single source of truth for every absolute path the agent must never read
    /// or write. Returned paths are literal (not regex-escaped); callers that build
    /// regex rules (the cp command policy) escape them as needed. Keeping the list in
    /// one place guarantees the read_file/write_file deny rules and the cp command
    /// deny rules stay in sync.
    /// </summary>
    private static List<string> CollectDeniedPaths(string home, string gemini, string cli)
    {
        var paths = new List<string>();

        // Sensitive system trees and binaries/libs — read and write both blocked.
        // Writes are denied to prevent tampering/persistence; reads are denied because
        // the agent has no legitimate need to read these and it only aids recon.
        string[] systemTrees =
        [
            "/app",
            "/bin",
            "/boot",
            "/dev",
            "/etc",
            "/lib",
            "/lib64",
            "/media",
            "/mnt",
            "/opt",
            "/proc",
            "/root",
            "/run",
            "/sbin",
            "/srv",
            "/sys",
            "/usr",
            "/var",
        ];
        paths.AddRange(systemTrees);

        // Home dotfiles/dirs that commonly hold credentials. Denied by name because
        // $HOME itself cannot be denied (the agy brain lives under it).
        string[] homeEntries =
        [
            ".ssh", ".openab", ".config", ".cache", ".local", ".npm",
            ".bashrc", ".profile", ".bash_history", ".bash_logout",
            ".gitconfig", ".netrc", ".aws", ".docker", ".kube",
        ];
        foreach (var entry in homeEntries)
        {
            paths.Add($"{home}/{entry}");
        }

        // ~/.gemini holds OAuth creds, account info, history and agy state. Every
        // entry is denied individually so the directory is fully locked down apart
        // from antigravity-cli/brain (which agy needs to stage generated files).
        string[] geminiEntries =
        [
            "oauth_creds.json", "google_accounts.json", "installation_id",
            "projects.json", "state.json", "trustedFolders.json",
            "mcp-server-enablement.json", "settings.json", "GEMINI.md",
            "antigravity/", "antigravity-backup", "antigravity-ide",
            "commands", "config", "extensions", "history", "policies", "tmp",
        ];
        foreach (var entry in geminiEntries)
        {
            paths.Add($"{gemini}/{entry}");
        }

        // Inside antigravity-cli deny everything sensitive but NOT brain:
        // conversation DBs (cross-chat history), the agy-acp session map, and our
        // own policy file (so the agent can never weaken its own sandbox). brain is
        // deliberately omitted — it is the only entry the agent is allowed to touch.
        string[] cliEntries =
        [
            "antigravity-oauth-token", "bin", "builtin", "cache", "cli.log",
            "conversations", "history.jsonl", "implicit", "installation_id",
            "keybindings.json", "knowledge", "last_check.timestamp", "log",
            "mcp", "mcp-server-enablement.json", "mcp_config.json", "scratch",
            "sessions.json", "settings.json", "updater",
        ];
        foreach (var entry in cliEntries)
        {
            paths.Add($"{cli}/{entry}");
        }

        return paths;
    }

    /// <summary>
    /// Appends the <c>command(...)</c> rules for the configured mode. The goal is to
    /// permit only a single file-delivery command of the exact shape
    /// <c>cp &lt;safe-src&gt; &lt;safe-dst&gt;</c> and deny everything else.
    /// </summary>
    private void AppendCommandRules(List<string> allow, List<string> deny, List<string> deniedPaths)
    {
        switch (_commandRuleMode)
        {
            case "off":
                // No command restrictions at all (agy default-allows). For testing only.
                return;

            case "denyall":
                deny.Add("command(*)");
                break;

            case "nolookahead":
                AppendNoLookaheadCommandRules(allow, deny, deniedPaths);
                break;

            default:
                // Unknown value falls back to the safe default.
                AppendNoLookaheadCommandRules(allow, deny, deniedPaths);
                break;
        }
    }

    /// <summary>
    /// RE2-safe strict whitelist with NO negative lookahead, expressed as a set of
    /// fully-anchored (<c>^...$</c>) deny patterns that each describe an ENTIRE bad
    /// command line. This construction is robust whether agy full-matches
    /// (<c>^(?:rule)$</c>) or partial/substring-matches its rules, and compiles on RE2
    /// (Go) engines. Validated against a positive/negative test matrix under both
    /// interpretations.
    ///
    /// The permitted shape is exactly <c>cp &lt;abs-no-meta-no-dotdot&gt; &lt;abs-no-meta-no-dotdot&gt;</c>
    /// (two arguments). The destination is not positively constrained to the output
    /// directory (that needs lookahead); functionally this is fine because only files
    /// that end up in the per-turn output directory are ever delivered, and the
    /// container read-only rootfs blocks writes elsewhere. Sensitive source roots are
    /// denied by name as a best-effort. This is defense-in-depth, not a boundary.
    /// </summary>
    private static void AppendNoLookaheadCommandRules(List<string> allow, List<string> deny, List<string> deniedPaths)
    {
        // agy treats command(...) and unsandboxed(...) as separate permission verbs,
        // so every rule body has to be registered under both. These helpers emit the
        // identical pattern for both verbs to avoid duplicating each line by hand.
        void AddAllow(string body)
        {
            allow.Add($"command({body})");
            allow.Add($"unsandboxed({body})");
        }

        void AddDeny(string body)
        {
            deny.Add($"command({body})");
            deny.Add($"unsandboxed({body})");
        }

        AddAllow("cp /tmp/pacos-agy/[-A-Za-z0-9._/]* /home/agent/.gemini/antigravity-cli/brain/[-A-Za-z0-9._/]*");
        AddAllow("cp /home/agent/.gemini/antigravity-cli/brain/[-A-Za-z0-9._/]* /tmp/pacos-agy/[-A-Za-z0-9._/]*");

        // --- Command is not the exact shape "cp <arg> <arg>" ---
        AddDeny("[^c].*");  // does not start with 'c'
        AddDeny("c$");      // just "c"
        AddDeny("c[^p].*"); // 'c' not followed by 'p'
        AddDeny("cp\\S+");  // "cp" followed by non-space

        AddDeny("cp \\S+ \\S+ \\S+"); // three or more arguments (chaining)
        AddDeny("cp \\S+ \\S+ \\S+ \\S+"); // three or more arguments (chaining)

        // --- Content of the (already 2-arg) command is unsafe ---
        AddDeny("cp .*[;&|\\$`()*?~\\\\'\"<>{}!#%].* \\S+"); // any shell metacharacter anywhere
        AddDeny("cp \\S+ .*[;&|\\$`()*?~\\\\'\"<>{}!#%].*"); // any shell metacharacter anywhere

        AddDeny("cp \\S+ .*\\.\\..*"); // path traversal ".." anywhere
        AddDeny("cp .*\\.\\..* \\S+"); // path traversal ".." anywhere

        AddDeny("cp [^/].* \\S+"); // source is not an absolute path
        AddDeny("cp \\S+ [^/].*"); // destination is not an absolute path

        // deny /./ segment:
        AddDeny("cp \\S+ .*/\\.(/|$).*");
        AddDeny("cp .*/\\.(/|$).* \\S+");

        // --- Deny every sensitive path from BuildSettingsJson in BOTH cp args ---
        // deniedPaths is the same list that drives the read_file/write_file denies,
        // so anything blocked there is automatically blocked as a cp source AND as a
        // cp destination. Paths are regex-escaped because command rules are regexes
        // (a literal '.' must not act as "any char"). Each space-separated token is
        // matched as an independent regex against the corresponding argument, so
        // "{path}.* .*" pins the source and ".* {path}.*" pins the destination.
        foreach (var path in deniedPaths)
        {
            var escaped = Regex.Escape(path);
            AddDeny($"cp {escaped}.* .*");
            AddDeny($"cp .* {escaped}.*");
        }
    }
}
