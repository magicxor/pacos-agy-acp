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

    private static readonly string[] ExpectedGalleryDlArgs = ["/opt/gallerydl-mcp/GalleryDl.McpServer.dll"];

    private static readonly string[] ExpectedFileMcpArgs = ["/opt/file-mcp/FileMcp.dll"];

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
        var json = AgyMcpConfigHostedService.BuildConfigJson(CreateOptions().McpServers, WorkspaceRoot);
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
        var json = AgyMcpConfigHostedService.BuildConfigJson(CreateOptions().McpServers, WorkspaceRoot);
        var filemcp = GetServer(json, "filemcp");

        Assert.Multiple(() =>
        {
            Assert.That(filemcp["command"]?.GetValue<string>(), Is.EqualTo("dotnet"));
            Assert.That(
                filemcp["args"]?.AsArray().Select(node => node?.GetValue<string>()),
                Is.EqualTo(ExpectedFileMcpArgs));

            // Both allow-list patterns are pinned to the workspace subtree via the
            // {workspaceRoot} placeholder; the baked appsettings.json empties the arrays
            // so a single index-0 override fully defines each side (no index 1).
            Assert.That(
                filemcp["env"]?["FileMove__AllowedSourcePatterns__0"]?.GetValue<string>(),
                Is.EqualTo($"^{WorkspaceRoot}(/.*)?$"));
            Assert.That(
                filemcp["env"]?["FileMove__AllowedTargetPatterns__0"]?.GetValue<string>(),
                Is.EqualTo($"^{WorkspaceRoot}(/.*)?$"));
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
    public void BuildConfigJson_SseServer_EmitsLowercaseTypeAndUrl()
    {
        var servers = new Dictionary<string, McpServer>
        {
            ["remote"] = new() { Type = ServerType.Sse, Url = "https://example.com/sse" },
        };

        var remote = GetServer(AgyMcpConfigHostedService.BuildConfigJson(servers, WorkspaceRoot), "remote");

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

        var json = AgyMcpConfigHostedService.BuildConfigJson(mcpServers, "/data/work");
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
