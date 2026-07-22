using System.Text.Json.Serialization;
using Pacos.Enums;

namespace Pacos.Models;

/// <summary>
/// One entry of agy's <c>mcp_config.json</c> (<c>~/.gemini/config/mcp_config.json</c>).
/// The observed on-disk format for stdio servers is just command/args/env — optional
/// members are nullable so that unset ones disappear from the generated JSON
/// (serialized with <c>WhenWritingNull</c>) and the file matches what agy itself writes.
/// </summary>
public sealed class McpServer
{
    /// <summary>Omitted from JSON when <see cref="ServerType.Unspecified"/> — stdio servers are implied by <c>command</c>.</summary>
    [JsonPropertyName("type")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public ServerType Type { get; set; }

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

    [JsonPropertyName("url")]
    public string? Url { get; set; }

    [JsonPropertyName("headers")]
    public Dictionary<string, string>? Headers { get; set; }
}
