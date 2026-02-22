using ClaudePermissionAnalyzer.Api.Models;

namespace ClaudePermissionAnalyzer.Api.Services;

public interface ILLMClient
{
    Task<LLMResponse> QueryAsync(string prompt, CancellationToken cancellationToken = default);
}

public class LLMResponse
{
    public int SafetyScore { get; set; }
    public string Reasoning { get; set; } = string.Empty;
    public string Category { get; set; } = "unknown";
    public bool Success { get; set; }
    public string? Error { get; set; }
    public long ElapsedMs { get; set; }
}
