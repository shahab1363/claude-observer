using System.Text.Json;
using System.Text.Json.Serialization;

namespace ClaudePermissionAnalyzer.Api.Models;

public class HookInput
{
    [JsonPropertyName("hookEventName")]
    public string HookEventName { get; set; } = string.Empty;

    [JsonPropertyName("sessionId")]
    public string SessionId { get; set; } = string.Empty;

    [JsonPropertyName("toolName")]
    public string? ToolName { get; set; }

    [JsonPropertyName("toolInput")]
    public JsonElement? ToolInput { get; set; }

    [JsonPropertyName("cwd")]
    public string? Cwd { get; set; }

    [JsonPropertyName("timestamp")]
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}
