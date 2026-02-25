using ClaudePermissionAnalyzer.Api.Models;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace ClaudePermissionAnalyzer.Api.Services;

public class ClaudeCliClient : LLMClientBase, ILLMClient
{
    private readonly LlmConfig _config;
    private readonly ConfigurationManager? _configManager;
    private readonly ILogger<ClaudeCliClient>? _logger;

    private const int DefaultTimeoutMs = 15_000;
    private const int MaxRetries = 3;
    private static readonly TimeSpan ModelNameRegexTimeout = TimeSpan.FromMilliseconds(100);

    public ClaudeCliClient(LlmConfig config, ILogger<ClaudeCliClient>? logger = null,
        ConfigurationManager? configManager = null, TerminalOutputService? terminalOutput = null)
        : base(configManager, config, terminalOutput)
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

    public async Task<LLMResponse> QueryAsync(string prompt, CancellationToken cancellationToken = default)
    {
        var timeout = CurrentTimeout;
        var promptPreview = PreviewPrompt(prompt);
        TerminalOutput?.Push("claude-cli", "info",
            $"Querying LLM ({prompt.Length} chars, timeout: {timeout}ms): {promptPreview}");
        var totalSw = Stopwatch.StartNew();

        for (int attempt = 1; attempt <= MaxRetries; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var sw = Stopwatch.StartNew();
            try
            {
                var args = BuildCommandArgs(prompt);
                var cmd = _configManager?.GetConfiguration()?.Llm?.Command;
                if (string.IsNullOrWhiteSpace(cmd)) cmd = "claude";
                var output = await ExecuteCommandAsync(cmd, args, timeout, cancellationToken).ConfigureAwait(false);
                sw.Stop();
                var response = ParseResponse(output);
                response.ElapsedMs = sw.ElapsedMilliseconds;
                _logger?.LogInformation("LLM query completed in {Elapsed}ms (timeout={Timeout}ms)", sw.ElapsedMilliseconds, timeout);
                TerminalOutput?.Push("claude-cli", "info", $"LLM query completed in {sw.ElapsedMilliseconds}ms");
                return response;
            }
            catch (TimeoutException) when (attempt < MaxRetries)
            {
                sw.Stop();
                TerminalOutput?.Push("claude-cli", "stderr",
                    $"Attempt {attempt}/{MaxRetries} timed out after {timeout}ms -- retrying...");
                _logger?.LogWarning("LLM query attempt {Attempt}/{MaxRetries} timed out after {Timeout}ms",
                    attempt, MaxRetries, timeout);
                await Task.Delay(500, cancellationToken).ConfigureAwait(false);
            }
            catch (TimeoutException)
            {
                totalSw.Stop();
                TerminalOutput?.Push("claude-cli", "stderr",
                    $"All {MaxRetries} attempts timed out ({totalSw.ElapsedMilliseconds}ms total)");
                return CreateTimeoutResponse("LLM query", MaxRetries, timeout, totalSw.ElapsedMilliseconds);
            }
            catch (InvalidOperationException) when (attempt < MaxRetries)
            {
                sw.Stop();
                TerminalOutput?.Push("claude-cli", "stderr",
                    $"Attempt {attempt}/{MaxRetries} failed -- retrying...");
                await Task.Delay(500, cancellationToken).ConfigureAwait(false);
            }
            catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 2)
            {
                return CreateFailureResponse(
                    "Claude CLI not found - ensure 'claude' command is installed and in PATH",
                    "Claude CLI is not installed or not in PATH");
            }
            catch (InvalidOperationException ex)
            {
                _logger?.LogError(ex, "LLM query failed after {MaxRetries} attempts", MaxRetries);
                return CreateFailureResponse("LLM query failed", "LLM query failed due to invalid operation");
            }
        }

        return CreateRetriesExhaustedResponse("LLM");
    }

    public static LLMResponse ParseResponse(string output)
    {
        try
        {
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
        // Note: isolation (no plugins, no hooks, no MCP) is handled by CLAUDE_CONFIG_DIR
        // pointing to the isolated config dir
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
    /// Has settings.json that disables all hooks, plugins, and MCP servers for fast startup.
    /// </summary>
    internal static string GetIsolatedConfigDir()
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".claude-permission-analyzer",
            "claude-subprocess");
        var settingsPath = Path.Combine(dir, "settings.json");

        if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

        // Always write settings to ensure plugins are disabled
        File.WriteAllText(settingsPath,
            """{"disableAllHooks":true,"enableAllProjectMcpServers":false,"enabledPlugins":{}}""");

        return dir;
    }

    /// <summary>
    /// Reads the Anthropic API key from the main Claude config (~/.claude/config.json).
    /// Returns null if not found.
    /// </summary>
    internal static string? ReadAnthropicApiKey()
    {
        try
        {
            var configPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".claude", "config.json");
            if (!File.Exists(configPath)) return null;

            var json = File.ReadAllText(configPath);
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("primaryApiKey", out var keyEl))
                return keyEl.GetString();
        }
        catch { /* non-fatal */ }
        return null;
    }

    /// <summary>
    /// Configures environment variables for a Claude subprocess:
    /// removes nesting-detection vars, sets isolated config dir, and provides the API key.
    /// </summary>
    internal static void ConfigureSubprocessEnvironment(ProcessStartInfo startInfo, string isolatedDir)
    {
        // Remove ALL Claude Code nesting-detection env vars
        foreach (var key in new[] { "CLAUDECODE", "CLAUDE_CODE_ENTRYPOINT", "CLAUDE_CODE_EXPERIMENTAL_AGENT_TEAMS" })
        {
            startInfo.Environment.Remove(key);
            startInfo.Environment[key] = null!;
        }

        // Point to isolated config dir (no plugins, no hooks)
        startInfo.Environment["CLAUDE_CONFIG_DIR"] = isolatedDir;

        // Provide API key from main config so subprocess can authenticate
        var apiKey = ReadAnthropicApiKey();
        if (!string.IsNullOrEmpty(apiKey))
        {
            startInfo.Environment["ANTHROPIC_API_KEY"] = apiKey;
        }
    }

    private async Task<string> ExecuteCommandAsync(
        string command,
        List<string> args,
        int timeoutMs,
        CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo { FileName = command };

        // Configure subprocess environment: remove nesting vars, set isolated config, provide API key
        var isolatedDir = GetIsolatedConfigDir();
        ConfigureSubprocessEnvironment(startInfo, isolatedDir);

        foreach (var arg in args)
        {
            startInfo.ArgumentList.Add(arg);
        }

        // Log the full command line (redact prompt arg which is the last one)
        var cmdLineArgs = args.Select((a, i) => i == args.Count - 1 ? $"\"{a[..Math.Min(80, a.Length)]}...\"" : $"\"{a}\"");
        var cmdLine = $"{command} {string.Join(" ", cmdLineArgs)}";
        TerminalOutput?.Push("claude-cli", "info", $"$ {cmdLine}");
        var hasApiKey = !string.IsNullOrEmpty(startInfo.Environment["ANTHROPIC_API_KEY"]);
        TerminalOutput?.Push("claude-cli", "info",
            $"  env: CLAUDE_CONFIG_DIR={isolatedDir}, ANTHROPIC_API_KEY={( hasApiKey ? "set" : "MISSING" )}");

        TerminalOutput?.Push("claude-cli", "info", "Waiting for response...");

        var result = await CliProcessRunner.RunAsync(
            startInfo, timeoutMs, "claude-cli", cancellationToken,
            _logger, TerminalOutput, enableHeartbeat: true
        ).ConfigureAwait(false);

        return result.Output;
    }
}
