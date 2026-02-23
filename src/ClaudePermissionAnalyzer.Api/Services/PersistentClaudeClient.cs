using ClaudePermissionAnalyzer.Api.Models;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace ClaudePermissionAnalyzer.Api.Services;

/// <summary>
/// LLM client that maintains a persistent claude CLI process using --print mode.
/// Sends /clear before each prompt to reset conversation context.
/// Supports configurable prompt prefix/suffix wrapping.
/// Falls back to spawning a new process if the persistent one fails.
/// </summary>
public class PersistentClaudeClient : ILLMClient, IDisposable
{
    private readonly LlmConfig _config;
    private readonly ILogger<PersistentClaudeClient> _logger;
    private readonly SemaphoreSlim _processLock = new(1, 1);
    private readonly ClaudeCliClient _fallbackClient;

    private Process? _persistentProcess;
    private StreamWriter? _stdin;
    private StreamReader? _stdout;
    private int _disposed;
    private int _consecutiveFailures;
    private const int MaxConsecutiveFailures = 3;
    private const int MaxOutputSize = 1_048_576; // 1MB

    public PersistentClaudeClient(LlmConfig config, ILogger<PersistentClaudeClient> logger)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _fallbackClient = new ClaudeCliClient(config, null);
    }

    public async Task<LLMResponse> QueryAsync(string prompt, CancellationToken cancellationToken = default)
    {
        if (Interlocked.CompareExchange(ref _disposed, 0, 0) != 0)
            throw new ObjectDisposedException(nameof(PersistentClaudeClient));

        // Build the full prompt with prefix/suffix
        var fullPrompt = BuildWrappedPrompt(prompt);

        // Try persistent process first
        await _processLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var result = await TryPersistentQueryAsync(fullPrompt, cancellationToken).ConfigureAwait(false);
            if (result != null)
            {
                Interlocked.Exchange(ref _consecutiveFailures, 0);
                return result;
            }
        }
        finally
        {
            _processLock.Release();
        }

        // Fallback to one-shot process
        _logger.LogWarning("Persistent process unavailable, falling back to one-shot mode");
        return await _fallbackClient.QueryAsync(fullPrompt, cancellationToken).ConfigureAwait(false);
    }

    private string BuildWrappedPrompt(string prompt)
    {
        var sb = new StringBuilder();

        if (!string.IsNullOrWhiteSpace(_config.PromptPrefix))
        {
            sb.AppendLine(_config.PromptPrefix);
            sb.AppendLine();
        }

        sb.Append(prompt);

        if (!string.IsNullOrWhiteSpace(_config.PromptSuffix))
        {
            sb.AppendLine();
            sb.AppendLine();
            sb.Append(_config.PromptSuffix);
        }

        return sb.ToString();
    }

    private async Task<LLMResponse?> TryPersistentQueryAsync(string prompt, CancellationToken cancellationToken)
    {
        try
        {
            // Ensure process is running
            if (!EnsureProcessRunning())
            {
                return null;
            }

            // Send /clear to reset context, then send the prompt
            // Use a unique end marker so we know when response is complete
            var endMarker = $"__END_{Guid.NewGuid():N}__";

            await _stdin!.WriteLineAsync("/clear").ConfigureAwait(false);
            await _stdin.FlushAsync(cancellationToken).ConfigureAwait(false);
            // Small delay to let /clear process
            await Task.Delay(200, cancellationToken).ConfigureAwait(false);

            // Send the actual prompt
            await _stdin.WriteLineAsync(prompt).ConfigureAwait(false);
            await _stdin.FlushAsync(cancellationToken).ConfigureAwait(false);

            // Read the response with timeout
            var response = await ReadResponseAsync(cancellationToken).ConfigureAwait(false);

            if (string.IsNullOrWhiteSpace(response))
            {
                _logger.LogWarning("Empty response from persistent process");
                Interlocked.Increment(ref _consecutiveFailures);
                if (_consecutiveFailures >= MaxConsecutiveFailures)
                {
                    _logger.LogWarning("Too many consecutive failures, restarting persistent process");
                    KillProcess();
                }
                return null;
            }

            return _fallbackClient.ParseResponse(response);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Persistent process query failed");
            Interlocked.Increment(ref _consecutiveFailures);
            if (_consecutiveFailures >= MaxConsecutiveFailures)
            {
                KillProcess();
            }
            return null;
        }
    }

    private async Task<string> ReadResponseAsync(CancellationToken cancellationToken)
    {
        var sb = new StringBuilder(512);
        var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(_config.Timeout);

        try
        {
            // Read lines until we get a complete JSON response or timeout
            // The claude process in interactive mode outputs the response then waits for next input
            // We detect response completion by looking for a complete JSON object
            var braceDepth = 0;
            var jsonStarted = false;

            while (!timeoutCts.Token.IsCancellationRequested)
            {
                // Use a short read timeout to avoid blocking forever
                var lineTask = _stdout!.ReadLineAsync(timeoutCts.Token);
                var line = await lineTask.ConfigureAwait(false);

                if (line == null)
                {
                    // Process ended
                    _logger.LogWarning("Persistent process stdout closed");
                    KillProcess();
                    break;
                }

                // Skip empty lines and common interactive prompts
                var trimmed = line.Trim();
                if (string.IsNullOrEmpty(trimmed))
                    continue;

                // Skip lines that look like interactive prompts (>, claude>, etc.)
                if (trimmed == ">" || trimmed.EndsWith("> ") || trimmed.StartsWith("claude"))
                    continue;

                sb.AppendLine(line);

                // Track JSON braces to detect complete response
                foreach (var ch in line)
                {
                    if (ch == '{')
                    {
                        braceDepth++;
                        jsonStarted = true;
                    }
                    else if (ch == '}')
                    {
                        braceDepth--;
                    }
                }

                // If we started a JSON object and braces are balanced, we have the complete response
                if (jsonStarted && braceDepth == 0)
                {
                    break;
                }

                // Safety: don't accumulate more than MaxOutputSize
                if (sb.Length > MaxOutputSize)
                {
                    _logger.LogWarning("Response exceeded max size, truncating");
                    break;
                }
            }
        }
        catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning("Read timed out after {Timeout}ms", _config.Timeout);
        }
        finally
        {
            timeoutCts.Dispose();
        }

        return sb.ToString();
    }

    private bool EnsureProcessRunning()
    {
        if (_persistentProcess != null && !_persistentProcess.HasExited)
            return true;

        // Clean up old process
        KillProcess();

        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "claude",
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };

            // Configure for non-interactive persistent use
            startInfo.ArgumentList.Add("--model");
            startInfo.ArgumentList.Add(_config.Model);
            startInfo.ArgumentList.Add("--permission-mode");
            startInfo.ArgumentList.Add("bypassPermissions");
            startInfo.ArgumentList.Add("--no-session-persistence");
            startInfo.ArgumentList.Add("--tools");
            startInfo.ArgumentList.Add("");  // Disable ALL tools
            startInfo.ArgumentList.Add("--strict-mcp-config");
            startInfo.ArgumentList.Add("--mcp-config");
            startInfo.ArgumentList.Add("{}");  // Disable all MCP servers
            startInfo.ArgumentList.Add("--verbose");

            if (!string.IsNullOrWhiteSpace(_config.SystemPrompt))
            {
                startInfo.ArgumentList.Add("--system-prompt");
                startInfo.ArgumentList.Add(_config.SystemPrompt);
            }

            // Remove CLAUDECODE env var to avoid nesting detection
            startInfo.Environment.Remove("CLAUDECODE");
            startInfo.Environment["CLAUDECODE"] = null!;

            // Use isolated config dir with no hooks/MCP servers for fast startup
            var isolatedDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".claude-permission-analyzer", "claude-subprocess");
            if (!Directory.Exists(isolatedDir)) Directory.CreateDirectory(isolatedDir);
            var settingsPath = Path.Combine(isolatedDir, "settings.json");
            if (!File.Exists(settingsPath))
                File.WriteAllText(settingsPath, "{}");
            startInfo.Environment["CLAUDE_CONFIG_DIR"] = isolatedDir;

            _persistentProcess = new Process { StartInfo = startInfo };

            if (!_persistentProcess.Start())
            {
                _logger.LogError("Failed to start persistent claude process");
                _persistentProcess = null;
                return false;
            }

            _stdin = _persistentProcess.StandardInput;
            _stdout = _persistentProcess.StandardOutput;

            // Consume stderr in background to prevent buffer blocking
            _ = Task.Run(async () =>
            {
                try
                {
                    while (!_persistentProcess.HasExited)
                    {
                        var line = await _persistentProcess.StandardError.ReadLineAsync().ConfigureAwait(false);
                        if (line != null)
                            _logger.LogDebug("[claude stderr] {Line}", line);
                    }
                }
                catch { /* process exited */ }
            });

            _logger.LogInformation("Started persistent claude process (PID: {Pid}, Model: {Model})",
                _persistentProcess.Id, _config.Model);

            // Give it a moment to initialize
            Thread.Sleep(1000);

            return !_persistentProcess.HasExited;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to start persistent claude process");
            KillProcess();
            return false;
        }
    }

    private void KillProcess()
    {
        try
        {
            if (_persistentProcess != null && !_persistentProcess.HasExited)
            {
                _logger.LogInformation("Killing persistent claude process (PID: {Pid})", _persistentProcess.Id);
                _persistentProcess.Kill(true);
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Error killing persistent process");
        }
        finally
        {
            _stdin = null;
            _stdout = null;
            _persistentProcess?.Dispose();
            _persistentProcess = null;
        }
    }

    public void Dispose()
    {
        if (Interlocked.CompareExchange(ref _disposed, 1, 0) != 0)
            return;

        KillProcess();
        _processLock.Dispose();
        GC.SuppressFinalize(this);
    }
}
