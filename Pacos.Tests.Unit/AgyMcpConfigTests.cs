using System.Text.Json.Nodes;
using Pacos.Constants;
using Pacos.Enums;
using Pacos.Models;
using Pacos.Models.Options;
using Pacos.Services.Acp;

namespace Pacos.Tests.Unit;

[TestFixture]
[Parallelizable(scope: ParallelScope.All)]
internal sealed class AgyMcpConfigTests
{
    private const string WorkspaceRoot = "/tmp/pacos-agy";

    private const string BrainDir = "/home/agent/.gemini/antigravity-cli/brain";

    private const string ApiToken = "test-crawl4ai-token";

    private static readonly string[] ExpectedGalleryDlArgs = ["/opt/gallerydl-mcp/GalleryDl.McpServer.dll"];

    private static readonly string[] ExpectedFileMcpArgs = ["/opt/file-mcp/FileMcp.dll"];

    private static readonly string[] ExpectedCrawl4AiArgs = ["/opt/crawl4ai-mcp/Crawl4AiMcp.dll"];

    private static PacosOptions CreateOptions() => new()
    {
        TelegramBotApiKey = "123:abc",
        AllowedChatIds = [1],
        ChatModel = "test-model",
    };

    private static JsonObject GetServer(string json, string name)
    {
        var servers = JsonNode.Parse(json)?["mcpServers"]?.AsObject()
                      ?? throw new InvalidOperationException("mcpServers object is missing");
        return servers[name]?.AsObject()
               ?? throw new InvalidOperationException($"server '{name}' is missing");
    }

    [Test]
    public void BuildConfigJson_DefaultGalleryDlServer_MatchesAgyOnDiskFormat()
    {
        var json = AgyMcpConfigHostedService.BuildConfigJson(CreateOptions().McpServers, WorkspaceRoot, BrainDir, ApiToken);
        var gallerydl = GetServer(json, "gallerydl");

        Assert.Multiple(() =>
        {
            Assert.That(gallerydl["command"]?.GetValue<string>(), Is.EqualTo("dotnet"));
            Assert.That(
                gallerydl["args"]?.AsArray().Select(node => node?.GetValue<string>()),
                Is.EqualTo(ExpectedGalleryDlArgs));
            Assert.That(
                gallerydl["env"]?["GalleryDlApi__BaseUrl"]?.GetValue<string>(),
                Is.EqualTo("http://gallerydl-webapi:8080"));
            Assert.That(gallerydl["env"]?["GalleryDlApi__MaxTake"]?.GetValue<string>(), Is.EqualTo("10"));

            // The image build empties the shipped AllowedPathPrefixes default, so a single
            // index-0 override fully defines the allow-list; no other indexes may appear.
            Assert.That(
                gallerydl["env"]?["GalleryDlApi__AllowedPathPrefixes__0"]?.GetValue<string>(),
                Is.EqualTo(WorkspaceRoot));
            Assert.That(
                gallerydl["env"]?.AsObject().ContainsKey("GalleryDlApi__AllowedPathPrefixes__1"),
                Is.False);

            // agy's own on-disk format for stdio entries is bare command/args/env:
            // no "type", "headers", "envFile" or "url" members may appear.
            Assert.That(gallerydl.ContainsKey("type"), Is.False);
            Assert.That(gallerydl.ContainsKey("headers"), Is.False);
            Assert.That(gallerydl.ContainsKey("envFile"), Is.False);
            Assert.That(gallerydl.ContainsKey("url"), Is.False);
        });
    }

    [Test]
    public void BuildConfigJson_DefaultFileMcpServer_MatchesAgyOnDiskFormat()
    {
        var json = AgyMcpConfigHostedService.BuildConfigJson(CreateOptions().McpServers, WorkspaceRoot, BrainDir, ApiToken);
        var filemcp = GetServer(json, "filemcp");

        Assert.Multiple(() =>
        {
            Assert.That(filemcp["command"]?.GetValue<string>(), Is.EqualTo("dotnet"));
            Assert.That(
                filemcp["args"]?.AsArray().Select(node => node?.GetValue<string>()),
                Is.EqualTo(ExpectedFileMcpArgs));

            // The source is pinned to the (regex-escaped) brain staging dir and the target
            // to the per-turn output dir under the workspace root; the baked appsettings.json
            // empties the arrays so a single index-0 override fully defines each side (no index 1).
            Assert.That(
                filemcp["env"]?["FileMove__AllowedSourcePatterns__0"]?.GetValue<string>(),
                Is.EqualTo("^/home/agent/\\.gemini/antigravity-cli/brain(/.*)?$"));
            Assert.That(
                filemcp["env"]?["FileMove__AllowedTargetPatterns__0"]?.GetValue<string>(),
                Is.EqualTo($"^{WorkspaceRoot}/[^/]+/\\.turns/[^/]+/output(/.*)?$"));
            Assert.That(
                filemcp["env"]?.AsObject().ContainsKey("FileMove__AllowedSourcePatterns__1"),
                Is.False);
            Assert.That(
                filemcp["env"]?.AsObject().ContainsKey("FileMove__AllowedTargetPatterns__1"),
                Is.False);
            Assert.That(filemcp["env"]?["FileMove__MaxFileAgeSeconds"]?.GetValue<string>(), Is.EqualTo("600"));

            // agy's own on-disk format for stdio entries is bare command/args/env:
            // no "type", "headers", "envFile" or "url" members may appear.
            Assert.That(filemcp.ContainsKey("type"), Is.False);
            Assert.That(filemcp.ContainsKey("headers"), Is.False);
            Assert.That(filemcp.ContainsKey("envFile"), Is.False);
            Assert.That(filemcp.ContainsKey("url"), Is.False);
        });
    }

    [Test]
    public void BuildConfigJson_DefaultCrawl4AiServer_MatchesAgyOnDiskFormat()
    {
        var json = AgyMcpConfigHostedService.BuildConfigJson(CreateOptions().McpServers, WorkspaceRoot, BrainDir, ApiToken);
        var crawl4ai = GetServer(json, "crawl4ai");

        Assert.Multiple(() =>
        {
            Assert.That(crawl4ai["command"]?.GetValue<string>(), Is.EqualTo("dotnet"));
            Assert.That(
                crawl4ai["args"]?.AsArray().Select(node => node?.GetValue<string>()),
                Is.EqualTo(ExpectedCrawl4AiArgs));

            // Backend URL points at the crawl4ai sidecar on the internal compose network.
            Assert.That(
                crawl4ai["env"]?["Crawl4Ai__BaseUrl"]?.GetValue<string>(),
                Is.EqualTo("http://crawl4ai:11235"));

            // The bearer-token placeholder is substituted with the configured Crawl4AiApiToken.
            Assert.That(
                crawl4ai["env"]?["Crawl4Ai__ApiToken"]?.GetValue<string>(),
                Is.EqualTo(ApiToken));

            // Writes are constrained to the per-turn output dir; the baked appsettings.json empties
            // the array so a single index-0 override fully defines the allow-list (no index 1).
            Assert.That(
                crawl4ai["env"]?["Crawl4Ai__AllowedOutputPatterns__0"]?.GetValue<string>(),
                Is.EqualTo($"^{WorkspaceRoot}/[^/]+/\\.turns/[^/]+/(output|temp)(/.*)?$"));
            Assert.That(
                crawl4ai["env"]?.AsObject().ContainsKey("Crawl4Ai__AllowedOutputPatterns__1"),
                Is.False);

            // agy's own on-disk format for stdio entries is bare command/args/env:
            // no "type", "headers", "envFile" or "url" members may appear.
            Assert.That(crawl4ai.ContainsKey("type"), Is.False);
            Assert.That(crawl4ai.ContainsKey("headers"), Is.False);
            Assert.That(crawl4ai.ContainsKey("envFile"), Is.False);
            Assert.That(crawl4ai.ContainsKey("url"), Is.False);
        });
    }

    [Test]
    public void BuildConfigJson_FileMcpTargetPattern_RegexEscapesConfigurableWorkspaceRoot()
    {
        // A user-configured WorkingDirectoryRoot may contain regex metacharacters; they must be
        // escaped inside the FileMove target regex, yet left raw in gallerydl's literal prefix.
        const string root = "/srv/p.g+1";
        var json = AgyMcpConfigHostedService.BuildConfigJson(CreateOptions().McpServers, root, BrainDir, ApiToken);

        Assert.Multiple(() =>
        {
            Assert.That(
                GetServer(json, "filemcp")["env"]?["FileMove__AllowedTargetPatterns__0"]?.GetValue<string>(),
                Is.EqualTo(@"^/srv/p\.g\+1/[^/]+/\.turns/[^/]+/output(/.*)?$"));
            Assert.That(
                GetServer(json, "gallerydl")["env"]?["GalleryDlApi__AllowedPathPrefixes__0"]?.GetValue<string>(),
                Is.EqualTo(root));
        });
    }

    [Test]
    public void ResolveRoot_TrimsTrailingSeparatorSoTargetPatternHasNoDoubleSlash()
    {
        // A configured root with a trailing separator must not leak a doubled separator into
        // the FileMove target regex (which would never match the real output path).
        var options = CreateOptions();
        options.WorkingDirectoryRoot = "/data/work/";

        var root = AcpSessionPool.ResolveRoot(options);
        var json = AgyMcpConfigHostedService.BuildConfigJson(options.McpServers, root, BrainDir, ApiToken);

        Assert.Multiple(() =>
        {
            Assert.That(root, Is.EqualTo("/data/work"));
            Assert.That(
                GetServer(json, "filemcp")["env"]?["FileMove__AllowedTargetPatterns__0"]?.GetValue<string>(),
                Is.EqualTo(@"^/data/work/[^/]+/\.turns/[^/]+/output(/.*)?$"));
        });
    }

    [Test]
    public void BuildConfigJson_SseServer_EmitsLowercaseTypeAndUrl()
    {
        var servers = new Dictionary<string, McpServer>
        {
            ["remote"] = new() { Type = ServerType.Sse, Url = "https://example.com/sse" },
        };

        var remote = GetServer(AgyMcpConfigHostedService.BuildConfigJson(servers, WorkspaceRoot, BrainDir, ApiToken), "remote");

        Assert.Multiple(() =>
        {
            Assert.That(remote["type"]?.GetValue<string>(), Is.EqualTo("sse"));
            Assert.That(remote["url"]?.GetValue<string>(), Is.EqualTo("https://example.com/sse"));
            Assert.That(remote.ContainsKey("command"), Is.False);
        });
    }

    [Test]
    public void BuildConfigJson_WorkspaceRootPlaceholder_SubstitutesWithoutMutatingSource()
    {
        var mcpServers = CreateOptions().McpServers;

        var json = AgyMcpConfigHostedService.BuildConfigJson(mcpServers, "/data/work", BrainDir, ApiToken);
        var env = GetServer(json, "gallerydl")["env"];

        Assert.Multiple(() =>
        {
            Assert.That(
                env?["GalleryDlApi__AllowedPathPrefixes__0"]?.GetValue<string>(),
                Is.EqualTo("/data/work"));
            Assert.That(
                env?["GalleryDlApi__BaseUrl"]?.GetValue<string>(),
                Is.EqualTo("http://gallerydl-webapi:8080"));
            Assert.That(
                mcpServers["gallerydl"].Env?["GalleryDlApi__AllowedPathPrefixes__0"],
                Is.EqualTo(Const.WorkspaceRootPlaceholder));
        });
    }
}
