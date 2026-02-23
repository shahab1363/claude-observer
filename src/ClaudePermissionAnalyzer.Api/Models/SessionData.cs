using System.Text.Json;
using System.Text.Json.Serialization;

namespace ClaudePermissionAnalyzer.Api.Models;

public class SessionData
{
    [JsonPropertyName("sessionId")]
    public string SessionId { get; set; }

    [JsonPropertyName("startTime")]
    public DateTime StartTime { get; set; }

    [JsonPropertyName("lastActivity")]
    public DateTime LastActivity { get; set; }

    [JsonPropertyName("workingDirectory")]
    public string? WorkingDirectory { get; set; }

    [JsonPropertyName("conversationHistory")]
    public List<SessionEvent> ConversationHistory { get; set; }

    public SessionData(string sessionId)
    {
        SessionId = sessionId;
        StartTime = DateTime.UtcNow;
        LastActivity = DateTime.UtcNow;
        ConversationHistory = new List<SessionEvent>();
    }
}

public class SessionEvent
{
    [JsonPropertyName("timestamp")]
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;

    [JsonPropertyName("toolName")]
    public string? ToolName { get; set; }

    [JsonPropertyName("toolInput")]
    public JsonElement? ToolInput { get; set; }

    [JsonPropertyName("decision")]
    public string? Decision { get; set; }

    [JsonPropertyName("safetyScore")]
    public int? SafetyScore { get; set; }

    [JsonPropertyName("reasoning")]
    public string? Reasoning { get; set; }

    [JsonPropertyName("category")]
    public string? Category { get; set; }

    [JsonPropertyName("content")]
    public string? Content { get; set; }

    [JsonPropertyName("handlerName")]
    public string? HandlerName { get; set; }

    [JsonPropertyName("promptTemplate")]
    public string? PromptTemplate { get; set; }

    [JsonPropertyName("threshold")]
    public int? Threshold { get; set; }

    [JsonPropertyName("provider")]
    public string? Provider { get; set; }
}
