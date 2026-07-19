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
/// Since agy 1.1.3/1.1.4 headless (<c>agy -p</c>) mode is fail-closed: it honors
/// this settings.json (permissions, file access, agent mode, artifact review) and
/// auto-denies any action that would require an interactive confirmation, so the
/// <c>allow</c> list below is load-bearing — everything the agent legitimately
/// needs (workspace/brain file I/O, the cp delivery command, web access) must be
/// explicitly allowed or the bot stops working.
///
/// Matching semantics (verified empirically against agy 1.1.4 headless runs):
/// precedence is Deny &gt; Ask &gt; Allow; file rules are literal absolute paths that
/// cover the whole subtree; <c>command(...)</c>/<c>unsandboxed(...)</c> targets are
/// matched LITERALLY unless the target starts with <c>regex:</c>. A regex target is
/// whitespace-tokenized, each token is an anchored RE2 regular expression
/// (<c>^(?:token)$</c>) matched against the corresponding command token, and rules
/// match as a token-prefix of the command (a bare-regex rule such as
/// <c>command(cp .*)</c> silently never matches — every regex-shaped rule below
/// MUST carry the <c>regex:</c> prefix, in allow and deny alike). The deny rules
/// are kept as defense-in-depth: they also override agy's built-in default command
/// grants (e.g. <c>command(cat)</c>) and protect against a future fail-open
/// regression. The real isolation boundary is still the container (read-only
/// rootfs, dropped capabilities, pids/memory/cpu limits, no-new-privileges,
/// egress allow-list).
///
/// The policy is the single source of truth and lives in code so it cannot be
/// forgotten or silently replaced by a mounted volume / stale image layer. If
/// the file cannot be written the application fails to start (fail-closed) so
/// the agent never runs with weaker-than-intended permissions.
///
/// NOTE: this policy is Linux-shaped and only fully works in the production
/// container. On a Windows dev machine agy feeds the file rules into the
/// run_command sandbox spec, which rejects the Linux deny paths (/app, /bin, …)
/// as non-absolute, and the "must start with /" cp denies fire on drive-letter
/// paths — so every terminal command fails locally even though file I/O works.
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
    private readonly string _chatModel;

    public AgySecurityPolicyHostedService(
        ILogger<AgySecurityPolicyHostedService> logger,
        IOptions<PacosOptions> options)
    {
        _logger = logger;
        _workspaceRoot = NormalizePath(AcpSessionPool.ResolveRoot(options.Value));
        _commandRuleMode = (options.Value.AgyCommandRuleMode ?? "nolookahead").Trim().ToLowerInvariant();
        _chatModel = options.Value.ChatModel;
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
    /// Windows too by converting backslashes and trimming a trailing slash. Drive
    /// letters must be KEPT: agy strips them symmetrically from rules and checked
    /// paths at match time, while the sandbox configuration for run_command feeds
    /// the allowlisted paths to the OS verbatim and rejects drive-less paths as
    /// non-absolute (verified on 1.1.4 on Windows).
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
        // file locations the agent may touch. The brain dir lies outside the
        // workspace, so without this explicit allow every write to it would be
        // auto-denied in headless mode.
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

        AppendCommandRules(allow, deny, deniedPaths, _workspaceRoot, $"{cli}/brain");

        // Headless agy (>= 1.1.3) honors these settings and auto-denies anything
        // that would need an interactive confirmation, so every review pause must
        // be resolved up front:
        // - agentMode "accept-edits": skip the request-review diff pause before
        //   file writes (permission deny rules still take precedence).
        // - toolPermission "request-review": allowlisted commands run, everything
        //   else asks and is therefore auto-denied in headless mode. "strict" is
        //   NOT usable here: it prompts even for allowlisted commands (verified on
        //   1.1.4), which headless mode turns into a blanket auto-deny.
        // - artifactReviewPolicy "always-proceed": never pause on agy's own plan/
        //   report artifacts (they live in the allowlisted brain dir anyway).
        // - trustedWorkspaces: pre-trust the workspace root (covers every per-chat
        //   subdirectory) so headless runs never hit the folder-trust gate.
        var policy = new
        {
            model = _chatModel,
            agentMode = "accept-edits",
            toolPermission = "request-review",
            artifactReviewPolicy = "always-proceed",
            trustedWorkspaces = new[] { _workspaceRoot },
            permissions = new { allow, deny },
        };
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
    private void AppendCommandRules(List<string> allow, List<string> deny, List<string> deniedPaths, string workspaceRoot, string brainDir)
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
                AppendNoLookaheadCommandRules(allow, deny, deniedPaths, workspaceRoot, brainDir);
                break;

            default:
                // Unknown value falls back to the safe default.
                AppendNoLookaheadCommandRules(allow, deny, deniedPaths, workspaceRoot, brainDir);
                break;
        }
    }

    /// <summary>
    /// RE2-safe strict whitelist with NO negative lookahead. Every rule body is
    /// emitted with the <c>regex:</c> target prefix — since agy 1.0.13 command
    /// targets are matched literally by default and a bare-regex rule silently
    /// never matches (verified on 1.1.4). With the prefix, agy whitespace-tokenizes
    /// the target, treats every token as an anchored RE2 regex (<c>^(?:token)$</c>)
    /// and matches rules as a token-prefix of the command, so these patterns behave
    /// the same whether the engine full-matches or prefix-matches. Validated
    /// against a positive/negative test matrix under both interpretations.
    ///
    /// The permitted shape is exactly <c>cp &lt;abs-no-meta-no-dotdot&gt; &lt;abs-no-meta-no-dotdot&gt;</c>
    /// (two arguments). Because allow rules prefix-match, the "three or more
    /// arguments" deny rules below are what stops the 3-token allow rule from also
    /// matching longer commands. The destination is not positively constrained to
    /// the output directory (that needs lookahead); functionally this is fine
    /// because only files that end up in the per-turn output directory are ever
    /// delivered, and the container read-only rootfs blocks writes elsewhere.
    /// Sensitive source roots are denied by name as a best-effort. agy 1.1.3+
    /// headless mode already auto-denies any command that matches no allow rule,
    /// so this deny wall is defense-in-depth (it also outranks agy's built-in
    /// default command grants such as <c>command(cat)</c>), not the boundary.
    /// </summary>
    private static void AppendNoLookaheadCommandRules(List<string> allow, List<string> deny, List<string> deniedPaths, string workspaceRoot, string brainDir)
    {
        // agy treats command(...) and unsandboxed(...) as separate permission verbs,
        // so every rule body has to be registered under both. These helpers emit the
        // identical pattern for both verbs to avoid duplicating each line by hand.
        // The regex: prefix is mandatory: without it the target is matched as a
        // literal command prefix and every pattern here would be dead.
        void AddAllow(string body)
        {
            allow.Add($"command(regex:{body})");
            allow.Add($"unsandboxed(regex:{body})");
        }

        void AddDeny(string body)
        {
            deny.Add($"command(regex:{body})");
            deny.Add($"unsandboxed(regex:{body})");
        }

        // The workspace root and the agy brain staging dir are resolved dynamically
        // (HOME and WorkingDirectoryRoot can vary), so the cp allowlist tracks the
        // same locations the file I/O policy grants. Escape them because command
        // rules are regexes (a literal '.' must not act as "any char").
        var workspace = Regex.Escape(workspaceRoot);
        var brain = Regex.Escape(brainDir);

        AddAllow($"cp {workspace}/[-A-Za-z0-9._/]* {brain}/[-A-Za-z0-9._/]*");
        AddAllow($"cp {brain}/[-A-Za-z0-9._/]* {workspace}/[-A-Za-z0-9._/]*");

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
