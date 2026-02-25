using ClaudePermissionAnalyzer.Api.Models;
using Microsoft.Extensions.Logging;

namespace ClaudePermissionAnalyzer.Api.Services;

/// <summary>
/// Shared infrastructure for all ILLMClient implementations.
/// Provides timeout resolution, terminal output helpers, and error response factories.
/// </summary>
public abstract class LLMClientBase
{
    protected const int MaxTimeoutMs = 300_000; // 5 minutes
    protected const int MaxOutputSize = 1_048_576; // 1MB

    private readonly LlmConfig? _initialConfig;
    private readonly ConfigurationManager? _configManager;

    protected readonly TerminalOutputService? TerminalOutput;

    protected LLMClientBase(
        ConfigurationManager? configManager,
        LlmConfig? initialConfig,
        TerminalOutputService? terminalOutput)
    {
        _configManager = configManager;
        _initialConfig = initialConfig;
        TerminalOutput = terminalOutput;
    }

    /// <summary>
    /// Resolves the current timeout from live config, falling back to initial config, then to 15000ms.
    /// Always clamped to [1000, MaxTimeoutMs].
    /// </summary>
    protected int CurrentTimeout
    {
        get
        {
            var timeout = _configManager?.GetConfiguration()?.Llm?.Timeout
                ?? _initialConfig?.Timeout
                ?? 15000;
            return Math.Clamp(timeout, 1000, MaxTimeoutMs);
        }
    }

    /// <summary>Creates a failed LLMResponse with the given error and reasoning.</summary>
    protected static LLMResponse CreateFailureResponse(string error, string reasoning, long elapsedMs = 0)
    {
        return new LLMResponse
        {
            Success = false,
            SafetyScore = 0,
            Error = error,
            Reasoning = reasoning,
            ElapsedMs = elapsedMs
        };
    }

    /// <summary>Creates a timeout failure response after all retries are exhausted.</summary>
    protected static LLMResponse CreateTimeoutResponse(string providerName, int maxRetries, int timeoutMs, long totalElapsedMs)
    {
        return new LLMResponse
        {
            Success = false,
            SafetyScore = 0,
            Error = $"{providerName} timed out after {maxRetries} attempts ({timeoutMs}ms each)",
            Reasoning = $"{providerName} timed out after {maxRetries} attempts ({timeoutMs}ms each)",
            ElapsedMs = totalElapsedMs
        };
    }

    /// <summary>Creates an exhausted-retries failure response.</summary>
    protected static LLMResponse CreateRetriesExhaustedResponse(string providerName)
    {
        return new LLMResponse
        {
            Success = false,
            SafetyScore = 0,
            Error = $"{providerName} query failed after all retries",
            Reasoning = "All retry attempts exhausted"
        };
    }

    /// <summary>Truncates a prompt for display in terminal output.</summary>
    protected static string PreviewPrompt(string prompt, int maxLength = 120)
    {
        return prompt.Length > maxLength ? prompt[..maxLength] + "..." : prompt;
    }
}
