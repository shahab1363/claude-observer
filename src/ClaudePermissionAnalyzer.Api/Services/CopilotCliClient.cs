using ClaudePermissionAnalyzer.Api.Models;
using Microsoft.Extensions.Logging;
using System.Diagnostics;

namespace ClaudePermissionAnalyzer.Api.Services;

/// <summary>
/// LLM client that uses a Copilot CLI command for safety analysis.
/// By default uses "copilot" but can be configured to "gh" (with "copilot" as first arg).
/// Parses text responses heuristically to produce structured safety scores.
/// </summary>
public class CopilotCliClient : LLMClientBase, ILLMClient
{
    private readonly LlmConfig _config;
    private readonly ConfigurationManager? _configManager;
    private readonly ILogger<CopilotCliClient>? _logger;

    private const int MaxRetries = 3;

    public CopilotCliClient(LlmConfig config, ILogger<CopilotCliClient>? logger = null,
        ConfigurationManager? configManager = null, TerminalOutputService? terminalOutput = null)
        : base(configManager, config, terminalOutput)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _logger = logger;
        _configManager = configManager;
    }

    /// <summary>
    /// Gets the CLI command to use. Defaults to "copilot".
    /// When set to "gh", "copilot" is passed as the first argument.
    /// </summary>
    private (string fileName, bool addCopilotArg) GetCommand()
    {
        var cmd = _configManager?.GetConfiguration()?.Llm?.Command ?? _config.Command;
        if (string.IsNullOrWhiteSpace(cmd))
            cmd = "copilot";

        if (cmd.Equals("gh", StringComparison.OrdinalIgnoreCase))
            return ("gh", true);

        return (cmd, false);
    }

    public async Task<LLMResponse> QueryAsync(string prompt, CancellationToken cancellationToken = default)
    {
        var timeout = CurrentTimeout;
        var totalSw = Stopwatch.StartNew();

        for (int attempt = 1; attempt <= MaxRetries; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var sw = Stopwatch.StartNew();
            try
            {
                var output = await ExecuteCopilotAsync(prompt, timeout, cancellationToken).ConfigureAwait(false);
                sw.Stop();

                var response = ParseTextResponse(output);
                response.ElapsedMs = sw.ElapsedMilliseconds;
                _logger?.LogInformation("Copilot CLI query completed in {Elapsed}ms", sw.ElapsedMilliseconds);
                return response;
            }
            catch (TimeoutException) when (attempt < MaxRetries)
            {
                sw.Stop();
                TerminalOutput?.Push("copilot-cli", "stderr",
                    $"Attempt {attempt}/{MaxRetries} timed out after {timeout}ms -- retrying...");
                await Task.Delay(500, cancellationToken).ConfigureAwait(false);
            }
            catch (TimeoutException)
            {
                totalSw.Stop();
                TerminalOutput?.Push("copilot-cli", "stderr",
                    $"All {MaxRetries} attempts timed out ({totalSw.ElapsedMilliseconds}ms total)");
                return CreateTimeoutResponse("Copilot CLI", MaxRetries, timeout, totalSw.ElapsedMilliseconds);
            }
            catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 2)
            {
                var (cmd, _) = GetCommand();
                return CreateFailureResponse(
                    $"CLI command '{cmd}' not found - ensure it is installed and in PATH",
                    $"CLI command '{cmd}' is not installed or not in PATH");
            }
            catch (InvalidOperationException) when (attempt < MaxRetries)
            {
                sw.Stop();
                TerminalOutput?.Push("copilot-cli", "stderr",
                    $"Attempt {attempt}/{MaxRetries} failed -- retrying...");
                await Task.Delay(500, cancellationToken).ConfigureAwait(false);
            }
            catch (InvalidOperationException ex)
            {
                _logger?.LogError(ex, "Copilot CLI query failed after {MaxRetries} attempts", MaxRetries);
                return CreateFailureResponse("Copilot CLI query failed",
                    "Copilot CLI query failed due to invalid operation");
            }
        }

        return CreateRetriesExhaustedResponse("Copilot CLI");
    }

    /// <summary>
    /// Parses unstructured text from copilot into a structured LLMResponse.
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
                Reasoning = "Empty response from Copilot CLI -- defaulting to conservative score",
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
            // No strong indicators -- conservative default
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

    private async Task<string> ExecuteCopilotAsync(string prompt, int timeoutMs, CancellationToken cancellationToken)
    {
        var (fileName, addCopilotArg) = GetCommand();

        var startInfo = new ProcessStartInfo { FileName = fileName };

        if (addCopilotArg)
            startInfo.ArgumentList.Add("copilot");
        startInfo.ArgumentList.Add("-p");
        startInfo.ArgumentList.Add(prompt);
        startInfo.ArgumentList.Add("-s");               // Silent: output only response, no stats
        startInfo.ArgumentList.Add("--allow-all-tools"); // Non-interactive: don't prompt for permissions
        startInfo.ArgumentList.Add("--no-custom-instructions"); // Don't load AGENTS.md etc.

        // Remove Claude Code nesting-detection env vars to avoid interference
        foreach (var key in new[] { "CLAUDECODE", "CLAUDE_CODE_ENTRYPOINT", "CLAUDE_CODE_EXPERIMENTAL_AGENT_TEAMS" })
        {
            startInfo.Environment.Remove(key);
            startInfo.Environment[key] = null!;
        }

        var cmdDesc = addCopilotArg ? $"{fileName} copilot" : fileName;
        TerminalOutput?.Push("copilot-cli", "info",
            $"$ {cmdDesc} -p \"{PreviewPrompt(prompt, 80)}\" -s --allow-all-tools --no-custom-instructions");

        var result = await CliProcessRunner.RunAsync(
            startInfo, timeoutMs, "copilot-cli", cancellationToken,
            _logger, TerminalOutput
        ).ConfigureAwait(false);

        return result.Output;
    }
}
