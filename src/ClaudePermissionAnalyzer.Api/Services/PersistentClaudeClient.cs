using ClaudePermissionAnalyzer.Api.Models;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.Text.Json;

namespace ClaudePermissionAnalyzer.Api.Services;

/// <summary>
/// LLM client that maintains a persistent claude CLI process using stream-json I/O.
/// Sends user messages as {"type":"user","message":{"role":"user","content":"..."}} on stdin,
/// reads {"type":"result",...} responses from stdout. Process stays alive between requests.
/// Falls back to one-shot ClaudeCliClient on failure.
/// </summary>
public class PersistentClaudeClient : LLMClientBase, ILLMClient, IDisposable
{
    private readonly LlmConfig _config;
    private readonly ConfigurationManager? _configManager;
    private readonly ILogger<PersistentClaudeClient> _logger;
    private readonly SemaphoreSlim _processLock = new(1, 1);
    private readonly ClaudeCliClient _fallbackClient;

    private Process? _process;
    private StreamWriter? _stdin;
    private StreamReader? _stdout;
    private int _disposed;
    private int _consecutiveFailures;
    private const int MaxConsecutiveFailures = 3;

    public PersistentClaudeClient(LlmConfig config, ILogger<PersistentClaudeClient> logger,
        TerminalOutputService? terminalOutput = null, ConfigurationManager? configManager = null)
        : base(configManager, config, terminalOutput)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _configManager = configManager;
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _fallbackClient = new ClaudeCliClient(config, logger: null, configManager: configManager, terminalOutput: terminalOutput);
    }

    public async Task<LLMResponse> QueryAsync(string prompt, CancellationToken cancellationToken = default)
    {
        if (Interlocked.CompareExchange(ref _disposed, 0, 0) != 0)
            throw new ObjectDisposedException(nameof(PersistentClaudeClient));

        await _processLock.WaitAsync(cancellationToken);
        try
        {
            var result = await TryStreamQueryAsync(prompt, cancellationToken);
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

        // Fallback to one-shot
        _logger.LogWarning("Persistent process query failed, falling back to one-shot claude CLI");
        TerminalOutput?.Push("persistent-claude", "stderr",
            "Persistent process unavailable -- falling back to one-shot claude CLI");
        return await _fallbackClient.QueryAsync(prompt, cancellationToken);
    }

    private async Task<LLMResponse?> TryStreamQueryAsync(string prompt, CancellationToken cancellationToken)
    {
        try
        {
            if (!await EnsureProcessRunningAsync(cancellationToken))
                return null;

            var stdin = _stdin ?? throw new InvalidOperationException("stdin is null despite process reported as running");
            var stdout = _stdout ?? throw new InvalidOperationException("stdout is null despite process reported as running");

            var timeout = CurrentTimeout;
            TerminalOutput?.Push("persistent-claude", "info",
                $"Sending prompt ({prompt.Length} chars, timeout: {timeout}ms): {PreviewPrompt(prompt)}");

            // Send user message in stream-json format
            var escaped = JsonSerializer.Serialize(prompt);
            var msg = $"{{\"type\":\"user\",\"message\":{{\"role\":\"user\",\"content\":{escaped}}}}}";
            await stdin.WriteLineAsync(msg);
            await stdin.FlushAsync(cancellationToken);

            // Read lines until we get a "result" type message
            var sw = Stopwatch.StartNew();
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(timeout);

            string? assistantText = null;
            bool gotResult = false;

            while (!timeoutCts.Token.IsCancellationRequested)
            {
                var line = await stdout.ReadLineAsync(timeoutCts.Token);

                if (line == null)
                {
                    _logger.LogError("Persistent Claude stdout closed -- process died");
                    TerminalOutput?.Push("persistent-claude", "stderr", "Process stdout closed -- process died");
                    KillProcess();
                    break;
                }

                if (string.IsNullOrWhiteSpace(line)) continue;

                TerminalOutput?.Push("persistent-claude", "stdout",
                    line.Length > 200 ? line[..200] + "..." : line);

                try
                {
                    using var doc = JsonDocument.Parse(line);
                    var root = doc.RootElement;
                    var type = root.GetProperty("type").GetString();

                    if (type == "assistant")
                    {
                        if (root.TryGetProperty("message", out var msgEl) &&
                            msgEl.TryGetProperty("content", out var contentEl) &&
                            contentEl.ValueKind == JsonValueKind.Array)
                        {
                            foreach (var block in contentEl.EnumerateArray())
                            {
                                if (block.TryGetProperty("type", out var blockType) &&
                                    blockType.GetString() == "text" &&
                                    block.TryGetProperty("text", out var textEl))
                                {
                                    assistantText = textEl.GetString();
                                }
                            }
                        }
                    }
                    else if (type == "result")
                    {
                        gotResult = true;
                        TerminalOutput?.Push("persistent-claude", "info",
                            $"Result received in {sw.ElapsedMilliseconds}ms");
                        break;
                    }
                }
                catch (JsonException ex)
                {
                    if (line.TrimStart().StartsWith('{'))
                        TerminalOutput?.Push("persistent-claude", "stderr",
                            $"Failed to parse JSON: {ex.Message}");
                }
            }

            sw.Stop();

            if (!gotResult)
            {
                _logger.LogWarning("No result from persistent Claude after {Elapsed}ms", sw.ElapsedMilliseconds);
                TerminalOutput?.Push("persistent-claude", "stderr",
                    $"No result after {sw.ElapsedMilliseconds}ms");
                IncrementFailuresAndMaybeKill();
                return null;
            }

            var parsed = ClaudeCliClient.ParseResponse(assistantText ?? "");
            parsed.ElapsedMs = sw.ElapsedMilliseconds;
            return parsed;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning("Persistent Claude read timed out after {Timeout}ms", CurrentTimeout);
            TerminalOutput?.Push("persistent-claude", "stderr",
                $"Read timed out after {CurrentTimeout}ms");
            IncrementFailuresAndMaybeKill();
            return null;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Persistent Claude query failed");
            TerminalOutput?.Push("persistent-claude", "stderr", $"Query failed: {ex.Message}");
            IncrementFailuresAndMaybeKill();
            return null;
        }
    }

    /// <summary>
    /// Increments the consecutive failure counter and kills the process if the threshold is reached.
    /// </summary>
    private void IncrementFailuresAndMaybeKill()
    {
        Interlocked.Increment(ref _consecutiveFailures);
        if (_consecutiveFailures >= MaxConsecutiveFailures)
            KillProcess();
    }

    private async Task<bool> EnsureProcessRunningAsync(CancellationToken cancellationToken = default)
    {
        if (_process != null && !_process.HasExited)
            return true;

        KillProcess();

        try
        {
            var cmd = _configManager?.GetConfiguration()?.Llm?.Command;
            if (string.IsNullOrWhiteSpace(cmd)) cmd = "claude";

            var startInfo = new ProcessStartInfo
            {
                FileName = cmd,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };

            // -p enables non-interactive print mode, which is REQUIRED for
            // --input-format and --output-format to take effect. Without -p,
            // the CLI enters interactive TTY mode and ignores format flags.
            startInfo.ArgumentList.Add("-p");
            startInfo.ArgumentList.Add("--model");
            startInfo.ArgumentList.Add(_config.Model);
            startInfo.ArgumentList.Add("--output-format");
            startInfo.ArgumentList.Add("stream-json");
            startInfo.ArgumentList.Add("--input-format");
            startInfo.ArgumentList.Add("stream-json");
            startInfo.ArgumentList.Add("--verbose");
            startInfo.ArgumentList.Add("--no-session-persistence");
            startInfo.ArgumentList.Add("--dangerously-skip-permissions");

            if (!string.IsNullOrWhiteSpace(_config.SystemPrompt))
            {
                startInfo.ArgumentList.Add("--system-prompt");
                startInfo.ArgumentList.Add(_config.SystemPrompt);
            }

            // Configure subprocess environment
            var isolatedDir = ClaudeCliClient.GetIsolatedConfigDir();
            ClaudeCliClient.ConfigureSubprocessEnvironment(startInfo, isolatedDir);

            // Log command
            var cmdArgs = new List<string>();
            foreach (var a in startInfo.ArgumentList) cmdArgs.Add($"\"{a}\"");
            TerminalOutput?.Push("persistent-claude", "info",
                $"$ {cmd} {string.Join(" ", cmdArgs)}");

            var hasApiKey = !string.IsNullOrEmpty(startInfo.Environment["ANTHROPIC_API_KEY"]);
            TerminalOutput?.Push("persistent-claude", "info",
                $"  env: CLAUDE_CONFIG_DIR={isolatedDir}, ANTHROPIC_API_KEY={(hasApiKey ? "set" : "MISSING")}");

            _process = new Process { StartInfo = startInfo };

            if (!_process.Start())
            {
                _logger.LogError("Failed to start persistent Claude process");
                _process = null;
                return false;
            }

            _stdin = _process.StandardInput;
            _stdout = _process.StandardOutput;

            // Consume stderr in background
            var stderrProcess = _process;
            _ = Task.Run(async () =>
            {
                try
                {
                    while (!stderrProcess.HasExited)
                    {
                        var line = await stderrProcess.StandardError.ReadLineAsync();
                        if (line != null)
                            TerminalOutput?.Push("persistent-claude", "stderr", line);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Persistent Claude stderr reader stopped");
                }
            });

            _logger.LogInformation("Persistent Claude process started (PID: {Pid}, Model: {Model})", _process.Id, _config.Model);
            TerminalOutput?.Push("persistent-claude", "info",
                $"Started persistent process (PID: {_process.Id}, Model: {_config.Model})");

            // In -p (print) mode with --input-format stream-json, the CLI does NOT
            // send an init message before receiving input. It's ready to accept
            // JSON messages on stdin immediately. No init-wait needed.

            // Brief check that the process didn't die immediately (bad args, missing binary, etc.)
            await Task.Delay(200, cancellationToken);
            if (_process.HasExited)
            {
                _logger.LogError("Process exited immediately with code {Code}", _process.ExitCode);
                TerminalOutput?.Push("persistent-claude", "stderr",
                    $"Process exited immediately with code {_process.ExitCode}. Check stderr above for details.");
                KillProcess();
                return false;
            }

            TerminalOutput?.Push("persistent-claude", "info", "Process ready (print mode, no init handshake)");
            return true;
        }
        catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 2)
        {
            _logger.LogError(ex, "Claude CLI not found");
            TerminalOutput?.Push("persistent-claude", "stderr",
                "Claude CLI not found -- ensure 'claude' command is installed and in PATH");
            KillProcess();
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to start persistent Claude process");
            TerminalOutput?.Push("persistent-claude", "stderr", $"Failed to start: {ex.Message}");
            KillProcess();
            return false;
        }
    }

    private void KillProcess()
    {
        try
        {
            if (_process != null && !_process.HasExited)
            {
                _logger.LogInformation("Killing persistent process (PID: {Pid})", _process.Id);
                _process.Kill(true);
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Exception while killing persistent process");
        }
        finally
        {
            _stdin = null;
            _stdout = null;
            _process?.Dispose();
            _process = null;
        }
    }

    public void Dispose()
    {
        if (Interlocked.CompareExchange(ref _disposed, 1, 0) != 0) return;
        KillProcess();
        _processLock.Dispose();
        GC.SuppressFinalize(this);
    }
}
