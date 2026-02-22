using ClaudePermissionAnalyzer.Api.Models;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace ClaudePermissionAnalyzer.Api.Services;

public class ClaudeCliClient : ILLMClient
{
    private readonly LlmConfig _config;
    private readonly ConfigurationManager? _configManager;
    private readonly ILogger<ClaudeCliClient>? _logger;

    private const int MaxTimeoutMs = 300_000; // 5 minutes
    private const int DefaultTimeoutMs = 15_000;
    private const int MaxOutputSize = 1_048_576; // 1MB max output from CLI
    private static readonly TimeSpan ModelNameRegexTimeout = TimeSpan.FromMilliseconds(100);

    public ClaudeCliClient(LlmConfig config, ILogger<ClaudeCliClient>? logger = null, ConfigurationManager? configManager = null)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _logger = logger;
        _configManager = configManager;

        // Validate model name to prevent injection via config
        if (!IsValidModelName(_config.Model))
        {
            throw new ArgumentException("Model name contains invalid characters", nameof(config));
        }
    }

    /// <summary>Gets the current timeout from live config, falling back to initial config.</summary>
    private int CurrentTimeout
    {
        get
        {
            var timeout = _configManager?.GetConfiguration()?.Llm?.Timeout ?? _config.Timeout;
            return Math.Clamp(timeout, 1000, MaxTimeoutMs);
        }
    }

    public async Task<LLMResponse> QueryAsync(string prompt, CancellationToken cancellationToken = default)
    {
        var timeout = CurrentTimeout;
        var sw = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            var args = BuildCommandArgs(prompt);
            var output = await ExecuteCommandAsync("claude", args, timeout, cancellationToken).ConfigureAwait(false);
            sw.Stop();
            var response = ParseResponse(output);
            response.ElapsedMs = sw.ElapsedMilliseconds;
            _logger?.LogInformation("LLM query completed in {Elapsed}ms (timeout={Timeout}ms)", sw.ElapsedMilliseconds, timeout);
            return response;
        }
        catch (TimeoutException ex)
        {
            sw.Stop();
            return new LLMResponse
            {
                Success = false,
                Error = $"LLM query timed out after {timeout}ms: {ex.Message}",
                SafetyScore = 0,
                Reasoning = $"LLM query timed out after {timeout}ms",
                ElapsedMs = sw.ElapsedMilliseconds
            };
        }
        catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 2)
        {
            return new LLMResponse
            {
                Success = false,
                Error = "Claude CLI not found - ensure 'claude' command is installed and in PATH",
                SafetyScore = 0,
                Reasoning = "Claude CLI is not installed or not in PATH"
            };
        }
        catch (InvalidOperationException ex)
        {
            _logger?.LogError(ex, "LLM query failed with invalid operation");
            return new LLMResponse
            {
                Success = false,
                Error = "LLM query failed",
                SafetyScore = 0,
                Reasoning = "LLM query failed due to invalid operation"
            };
        }
        // Let other exceptions propagate - they indicate programming errors or system failures
    }

    public LLMResponse ParseResponse(string output)
    {
        try
        {
            // Enforce output size limit
            if (output.Length > MaxOutputSize)
            {
                return CreateErrorResponse("LLM output exceeded maximum size limit");
            }

            var jsonString = ExtractJsonFromOutput(output);
            if (jsonString == null)
            {
                return CreateErrorResponse("No JSON object found");
            }

            return ParseJsonToResponse(jsonString);
        }
        catch (JsonException ex)
        {
            return CreateErrorResponse($"Invalid JSON format: {ex.Message}");
        }
        catch (KeyNotFoundException ex)
        {
            return CreateErrorResponse($"Missing required field: {ex.Message}");
        }
    }

    private static string? ExtractJsonFromOutput(string output)
    {
        var startIdx = output.IndexOf('{');
        if (startIdx < 0)
        {
            return null;
        }

        // Try simple substring first
        try
        {
            using var testDoc = JsonDocument.Parse(output.AsMemory(startIdx));
            return output.Substring(startIdx);
        }
        catch (JsonException)
        {
            // If that fails, extract by brace counting
            return ExtractJsonByBraceCounting(output, startIdx);
        }
    }

    private static string? ExtractJsonByBraceCounting(string output, int startIdx)
    {
        int braceCount = 0;
        int endIdx = startIdx;

        for (int i = startIdx; i < output.Length; i++)
        {
            if (output[i] == '{') braceCount++;
            else if (output[i] == '}')
            {
                braceCount--;
                if (braceCount == 0)
                {
                    endIdx = i + 1;
                    break;
                }
            }
        }

        return braceCount == 0 ? output.Substring(startIdx, endIdx - startIdx) : null;
    }

    private static LLMResponse ParseJsonToResponse(string jsonString)
    {
        using var doc = JsonDocument.Parse(jsonString);
        var root = doc.RootElement;

        var safetyScore = root.GetProperty("safetyScore").GetInt32();
        // Clamp to valid range - defense against LLM returning out-of-range values
        safetyScore = Math.Clamp(safetyScore, 0, 100);

        var category = root.GetProperty("category").GetString() ?? "unknown";
        // Validate category is one of the known values
        if (!IsValidCategory(category))
        {
            category = "unknown";
        }

        return new LLMResponse
        {
            Success = true,
            SafetyScore = safetyScore,
            Reasoning = root.GetProperty("reasoning").GetString() ?? "No reasoning provided",
            Category = category
        };
    }

    private static bool IsValidCategory(string category)
    {
        return category is "safe" or "cautious" or "risky" or "dangerous" or "unknown" or "error";
    }

    private static LLMResponse CreateErrorResponse(string reasoning)
    {
        return new LLMResponse
        {
            Success = false,
            SafetyScore = 0,
            Reasoning = reasoning
        };
    }

    private static bool IsValidModelName(string model)
    {
        if (string.IsNullOrWhiteSpace(model) || model.Length > 64)
            return false;

        try
        {
            // Only allow alphanumeric, hyphens, dots, and underscores
            return Regex.IsMatch(model, @"^[a-zA-Z0-9\-._]+$", RegexOptions.None, ModelNameRegexTimeout);
        }
        catch (RegexMatchTimeoutException)
        {
            return false;
        }
    }

    private List<string> BuildCommandArgs(string prompt)
    {
        // Use ArgumentList for proper escaping - prevents command injection
        var args = new List<string>
        {
            "-p",  // Print mode: output response and exit (non-interactive)
            "--model",
            _config.Model,
            "--output-format",
            "text",
            "--no-session-persistence",  // Don't save to session history
        };

        if (!string.IsNullOrWhiteSpace(_config.SystemPrompt))
        {
            args.Add("--system-prompt");
            args.Add(_config.SystemPrompt);
        }

        args.Add(prompt);
        return args;
    }

    /// <summary>
    /// Gets an isolated Claude config directory for the analyzer subprocess.
    /// Has settings.json that disables all hooks and MCP servers for fast startup.
    /// </summary>
    private static string GetIsolatedConfigDir()
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".claude-permission-analyzer",
            "claude-subprocess");
        var settingsPath = Path.Combine(dir, "settings.json");

        if (!File.Exists(settingsPath))
        {
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
            // Minimal settings: disable all hooks and MCP servers
            File.WriteAllText(settingsPath, """
                {
                  "disableAllHooks": true,
                  "enableAllProjectMcpServers": false
                }
                """);
        }

        return dir;
    }

    private async Task<string> ExecuteCommandAsync(
        string command,
        List<string> args,
        int timeoutMs,
        CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = command,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        // Remove CLAUDECODE env var to allow claude CLI to run
        // (it refuses to start inside another Claude Code session)
        startInfo.Environment.Remove("CLAUDECODE");
        startInfo.Environment["CLAUDECODE"] = null!;

        // Use isolated config dir with no hooks/MCP servers for fast startup
        var isolatedDir = GetIsolatedConfigDir();
        startInfo.Environment["CLAUDE_CONFIG_DIR"] = isolatedDir;

        // Use ArgumentList instead of Arguments for proper escaping
        foreach (var arg in args)
        {
            startInfo.ArgumentList.Add(arg);
        }

        using var process = new Process
        {
            StartInfo = startInfo
        };

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
                    _logger?.LogWarning("CLI output exceeded {MaxSize} bytes, truncating", MaxOutputSize);
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
            throw new InvalidOperationException($"Failed to start process: {command}");
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
                    await Task.Delay(100).ConfigureAwait(false); // Give process time to die
                }
            }
            catch (Exception killEx)
            {
                _logger?.LogError(killEx, "CRITICAL: Failed to kill hung process - system may be experiencing resource exhaustion");
                throw new InvalidOperationException(
                    $"Command timed out after {timeoutMs}ms and process cleanup failed. " +
                    $"System may be experiencing resource exhaustion.", killEx);
            }

            throw new TimeoutException($"Command timed out after {timeoutMs}ms");
        }

        // Cancel async readers and wait for them to complete
        process.CancelOutputRead();
        process.CancelErrorRead();

        // Ensure all async output handlers have completed
        process.WaitForExit();

        if (process.ExitCode != 0)
        {
            var errorMessage = error.ToString().Trim();
            if (string.IsNullOrEmpty(errorMessage))
            {
                errorMessage = $"Process exited with code {process.ExitCode}";
            }

            throw new InvalidOperationException($"Command failed with exit code {process.ExitCode}: {errorMessage}");
        }

        return output.ToString();
    }
}
