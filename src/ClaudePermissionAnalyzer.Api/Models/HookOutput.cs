using System.Text.Json.Serialization;

namespace ClaudePermissionAnalyzer.Api.Models;

public class HookOutput
{
    [JsonPropertyName("autoApprove")]
    public bool AutoApprove { get; set; }

    [JsonPropertyName("safetyScore")]
    public int SafetyScore { get; set; }

    [JsonPropertyName("reasoning")]
    public string Reasoning { get; set; } = string.Empty;

    [JsonPropertyName("category")]
    public string Category { get; set; } = "unknown";

    [JsonPropertyName("threshold")]
    public int Threshold { get; set; }

    [JsonPropertyName("systemMessage")]
    public string? SystemMessage { get; set; }

    [JsonPropertyName("interrupt")]
    public bool Interrupt { get; set; }

    [JsonPropertyName("elapsedMs")]
    public long ElapsedMs { get; set; }
}
