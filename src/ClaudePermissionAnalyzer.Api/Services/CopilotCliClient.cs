using ClaudePermissionAnalyzer.Api.Models;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;

namespace ClaudePermissionAnalyzer.Api.Services;

/// <summary>
/// LLM client that uses GitHub Copilot CLI (gh copilot explain) for safety analysis.
/// Parses text responses heuristically to produce structured safety scores.
/// </summary>
public class CopilotCliClient : ILLMClient
{
    private readonly LlmConfig _config;
    private readonly ILogger<CopilotCliClient>? _logger;

    private const int MaxTimeoutMs = 300_000;
    private const int MaxOutputSize = 1_048_576;

    public CopilotCliClient(LlmConfig config, ILogger<CopilotCliClient>? logger = null)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _logger = logger;
    }

    public async Task<LLMResponse> QueryAsync(string prompt, CancellationToken cancellationToken = default)
    {
        var timeout = Math.Clamp(_config.Timeout, 1000, MaxTimeoutMs);
        var sw = Stopwatch.StartNew();

        try
        {
            var output = await ExecuteGhCopilotAsync(prompt, timeout, cancellationToken).ConfigureAwait(false);
            sw.Stop();

            var response = ParseTextResponse(output);
            response.ElapsedMs = sw.ElapsedMilliseconds;
            _logger?.LogInformation("Copilot CLI query completed in {Elapsed}ms", sw.ElapsedMilliseconds);
            return response;
        }
        catch (TimeoutException ex)
        {
            sw.Stop();
            return new LLMResponse
            {
                Success = false,
                Error = $"Copilot CLI query timed out after {timeout}ms: {ex.Message}",
                SafetyScore = 0,
                Reasoning = $"Copilot CLI query timed out after {timeout}ms",
                ElapsedMs = sw.ElapsedMilliseconds
            };
        }
        catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 2)
        {
            return new LLMResponse
            {
                Success = false,
                Error = "GitHub CLI not found - ensure 'gh' command is installed and in PATH with Copilot extension",
                SafetyScore = 0,
                Reasoning = "GitHub CLI (gh) is not installed or not in PATH"
            };
        }
        catch (InvalidOperationException ex)
        {
            _logger?.LogError(ex, "Copilot CLI query failed");
            return new LLMResponse
            {
                Success = false,
                Error = "Copilot CLI query failed",
                SafetyScore = 0,
                Reasoning = "Copilot CLI query failed due to invalid operation"
            };
        }
    }

    /// <summary>
    /// Parses unstructured text from gh copilot into a structured LLMResponse.
    /// Uses keyword heuristics since Copilot doesn't return JSON.
    /// </summary>
    internal LLMResponse ParseTextResponse(string output)
    {
        if (string.IsNullOrWhiteSpace(output))
        {
            return new LLMResponse
            {
                Success = false,
                SafetyScore = 50,
                Reasoning = "Empty response from Copilot CLI — defaulting to conservative score",
                Category = "cautious"
            };
        }

        if (output.Length > MaxOutputSize)
        {
            return new LLMResponse
            {
                Success = false,
                SafetyScore = 0,
                Reasoning = "Copilot output exceeded maximum size limit"
            };
        }

        var lower = output.ToLowerInvariant();

        // Count dangerous indicators
        var dangerousKeywords = new[] { "dangerous", "malicious", "destructive", "rm -rf", "format", "drop table", "injection", "exploit", "vulnerability" };
        var riskyKeywords = new[] { "risky", "caution", "careful", "warning", "elevated", "sudo", "admin", "sensitive", "credentials", "password", "secret" };
        var safeKeywords = new[] { "safe", "harmless", "benign", "standard", "normal", "routine", "read-only", "readonly", "no risk", "low risk" };

        int dangerCount = dangerousKeywords.Count(k => lower.Contains(k));
        int riskyCount = riskyKeywords.Count(k => lower.Contains(k));
        int safeCount = safeKeywords.Count(k => lower.Contains(k));

        int score;
        string category;

        if (dangerCount >= 2)
        {
            score = 15;
            category = "dangerous";
        }
        else if (dangerCount >= 1)
        {
            score = 30;
            category = "risky";
        }
        else if (riskyCount >= 2)
        {
            score = 50;
            category = "cautious";
        }
        else if (riskyCount >= 1 && safeCount == 0)
        {
            score = 60;
            category = "cautious";
        }
        else if (safeCount >= 2)
        {
            score = 90;
            category = "safe";
        }
        else if (safeCount >= 1)
        {
            score = 80;
            category = "safe";
        }
        else
        {
            // No strong indicators — conservative default
            score = 50;
            category = "cautious";
        }

        // Truncate reasoning to first 500 chars of output
        var reasoning = output.Length > 500 ? output[..500] + "..." : output;
        reasoning = reasoning.Replace('\n', ' ').Replace('\r', ' ').Trim();

        return new LLMResponse
        {
            Success = true,
            SafetyScore = score,
            Reasoning = reasoning,
            Category = category
        };
    }

    private async Task<string> ExecuteGhCopilotAsync(string prompt, int timeoutMs, CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "gh",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        startInfo.ArgumentList.Add("copilot");
        startInfo.ArgumentList.Add("explain");
        startInfo.ArgumentList.Add(prompt);

        // Remove CLAUDECODE env var to avoid interference
        startInfo.Environment.Remove("CLAUDECODE");
        startInfo.Environment["CLAUDECODE"] = null!;

        using var process = new Process { StartInfo = startInfo };

        var output = new StringBuilder(512);
        var error = new StringBuilder(256);
        var outputTruncated = false;

        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data != null && !outputTruncated)
            {
                if (output.Length + e.Data.Length > MaxOutputSize)
                {
                    outputTruncated = true;
                    _logger?.LogWarning("Copilot CLI output exceeded {MaxSize} bytes, truncating", MaxOutputSize);
                }
                else
                {
                    output.AppendLine(e.Data);
                }
            }
        };
        process.ErrorDataReceived += (_, e) => { if (e.Data != null) error.AppendLine(e.Data); };

        if (!process.Start())
        {
            throw new InvalidOperationException("Failed to start gh copilot process");
        }

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(timeoutMs);

        try
        {
            await process.WaitForExitAsync(cts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            try
            {
                if (!process.HasExited)
                {
                    process.Kill(true);
                    await Task.Delay(100).ConfigureAwait(false);
                }
            }
            catch (Exception killEx)
            {
                _logger?.LogError(killEx, "Failed to kill hung gh copilot process");
                throw new InvalidOperationException(
                    $"Copilot CLI timed out after {timeoutMs}ms and process cleanup failed.", killEx);
            }

            throw new TimeoutException($"Copilot CLI timed out after {timeoutMs}ms");
        }

        process.CancelOutputRead();
        process.CancelErrorRead();
        process.WaitForExit();

        if (process.ExitCode != 0)
        {
            var errorMessage = error.ToString().Trim();
            if (string.IsNullOrEmpty(errorMessage))
            {
                errorMessage = $"gh copilot exited with code {process.ExitCode}";
            }
            throw new InvalidOperationException($"gh copilot failed with exit code {process.ExitCode}: {errorMessage}");
        }

        return output.ToString();
    }
}
