using System.Text.Json.Serialization;

namespace ClaudePermissionAnalyzer.Api.Models;

public class AdaptiveThresholdData
{
    [JsonPropertyName("overrides")]
    public List<ThresholdOverride> Overrides { get; set; } = new();

    [JsonPropertyName("toolStats")]
    public Dictionary<string, ToolThresholdStats> ToolStats { get; set; } = new();

    [JsonPropertyName("lastCalculated")]
    public DateTime LastCalculated { get; set; } = DateTime.UtcNow;
}

public class ThresholdOverride
{
    [JsonPropertyName("timestamp")]
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    [JsonPropertyName("toolName")]
    public string ToolName { get; set; } = string.Empty;

    [JsonPropertyName("originalDecision")]
    public string OriginalDecision { get; set; } = string.Empty;

    [JsonPropertyName("userAction")]
    public string UserAction { get; set; } = string.Empty;

    [JsonPropertyName("safetyScore")]
    public int SafetyScore { get; set; }

    [JsonPropertyName("threshold")]
    public int Threshold { get; set; }

    [JsonPropertyName("sessionId")]
    public string SessionId { get; set; } = string.Empty;
}

public class ToolThresholdStats
{
    [JsonPropertyName("toolName")]
    public string ToolName { get; set; } = string.Empty;

    [JsonPropertyName("totalDecisions")]
    public int TotalDecisions { get; set; }

    [JsonPropertyName("overrideCount")]
    public int OverrideCount { get; set; }

    [JsonPropertyName("falsePositives")]
    public int FalsePositives { get; set; }

    [JsonPropertyName("falseNegatives")]
    public int FalseNegatives { get; set; }

    [JsonPropertyName("suggestedThreshold")]
    public int? SuggestedThreshold { get; set; }

    [JsonPropertyName("averageSafetyScore")]
    public double AverageSafetyScore { get; set; }

    [JsonPropertyName("confidenceLevel")]
    public double ConfidenceLevel { get; set; }
}
