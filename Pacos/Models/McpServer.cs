using System.Text.Json.Serialization;

namespace Pacos.Models;

/// <summary>
/// One entry of agy's <c>mcp_config.json</c> (<c>~/.gemini/config/mcp_config.json</c>).
/// The observed on-disk format for stdio servers is just command/args/env; a remote
/// (Streamable HTTP / SSE) server is <c>serverUrl</c> plus optional <c>headers</c>, with the
/// transport inferred from the URL — agy has no <c>type</c> member and rejects the legacy
/// <c>url</c> / <c>httpUrl</c> spellings. Optional members are nullable so that unset ones
/// disappear from the generated JSON (serialized with <c>WhenWritingNull</c>) and the file
/// matches what agy itself writes.
/// </summary>
public sealed class McpServer
{
    [JsonIgnore]
    public string? Name { get; set; }

    [JsonPropertyName("command")]
    public string? Command { get; set; }

    [JsonPropertyName("args")]
    public string[]? Args { get; set; }

    [JsonPropertyName("env")]
    public Dictionary<string, string?>? Env { get; set; }

    [JsonPropertyName("envFile")]
    public string? EnvFile { get; set; }

    [JsonPropertyName("serverUrl")]
    public string? ServerUrl { get; set; }

    [JsonPropertyName("headers")]
    public Dictionary<string, string>? Headers { get; set; }
}
