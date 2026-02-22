using System.Text.Json.Serialization;

namespace ClaudePermissionAnalyzer.Api.Models;

public class PermissionProfile
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;

    [JsonPropertyName("defaultThreshold")]
    public int DefaultThreshold { get; set; } = 85;

    [JsonPropertyName("autoApproveEnabled")]
    public bool AutoApproveEnabled { get; set; } = true;

    [JsonPropertyName("thresholdOverrides")]
    public Dictionary<string, int> ThresholdOverrides { get; set; } = new();

    public static readonly Dictionary<string, PermissionProfile> BuiltInProfiles = new()
    {
        ["strict"] = new PermissionProfile
        {
            Name = "Strict",
            Description = "High security - only the safest operations are auto-approved",
            DefaultThreshold = 95,
            AutoApproveEnabled = true,
            ThresholdOverrides = new Dictionary<string, int>
            {
                ["Bash"] = 98,
                ["Write"] = 96,
                ["Edit"] = 95,
                ["Read"] = 90
            }
        },
        ["moderate"] = new PermissionProfile
        {
            Name = "Moderate",
            Description = "Balanced security - reasonable operations are auto-approved",
            DefaultThreshold = 85,
            AutoApproveEnabled = true,
            ThresholdOverrides = new Dictionary<string, int>
            {
                ["Bash"] = 90,
                ["Write"] = 88,
                ["Edit"] = 85,
                ["Read"] = 75
            }
        },
        ["permissive"] = new PermissionProfile
        {
            Name = "Permissive",
            Description = "Low friction - most operations are auto-approved",
            DefaultThreshold = 70,
            AutoApproveEnabled = true,
            ThresholdOverrides = new Dictionary<string, int>
            {
                ["Bash"] = 80,
                ["Write"] = 75,
                ["Edit"] = 70,
                ["Read"] = 60
            }
        },
        ["lockdown"] = new PermissionProfile
        {
            Name = "Lockdown",
            Description = "Maximum security - nothing is auto-approved",
            DefaultThreshold = 100,
            AutoApproveEnabled = false,
            ThresholdOverrides = new Dictionary<string, int>()
        }
    };
}

public class ProfileConfig
{
    [JsonPropertyName("activeProfile")]
    public string ActiveProfile { get; set; } = "moderate";

    [JsonPropertyName("customProfiles")]
    public Dictionary<string, PermissionProfile> CustomProfiles { get; set; } = new();
}
