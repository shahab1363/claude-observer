using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.Text;

namespace ClaudePermissionAnalyzer.Api.Services;

/// <summary>
/// Result of a CLI process execution.
/// </summary>
public class CliProcessResult
{
    public required string Output { get; init; }
    public required string Error { get; init; }
    public required int ExitCode { get; init; }
}

/// <summary>
/// Runs a CLI subprocess with timeout, output size limits, and clean cancellation.
/// Consolidates the shared process-execution logic from ClaudeCliClient and CopilotCliClient.
/// </summary>
public static class CliProcessRunner
{
    private const int MaxOutputSize = 1_048_576; // 1MB

    /// <summary>
    /// Starts a process with the given start info, collects stdout/stderr, and waits
    /// with the specified timeout. Kills the process on timeout or caller cancellation.
    /// </summary>
    /// <param name="startInfo">Fully configured ProcessStartInfo (FileName, ArgumentList, Environment, etc.).</param>
    /// <param name="timeoutMs">Maximum time to wait for the process to exit.</param>
    /// <param name="sourceName">Label for terminal output lines (e.g. "claude-cli").</param>
    /// <param name="cancellationToken">Caller cancellation token.</param>
    /// <param name="logger">Optional logger for warnings/errors.</param>
    /// <param name="terminalOutput">Optional terminal output service for real-time streaming.</param>
    /// <param name="enableHeartbeat">When true, pushes periodic status lines to terminal output.</param>
    /// <returns>The process output.</returns>
    /// <exception cref="TimeoutException">Thrown when the process does not exit within timeoutMs.</exception>
    /// <exception cref="OperationCanceledException">Thrown when the caller cancels.</exception>
    /// <exception cref="InvalidOperationException">Thrown when the process fails to start or exits with non-zero code.</exception>
    public static async Task<CliProcessResult> RunAsync(
        ProcessStartInfo startInfo,
        int timeoutMs,
        string sourceName,
        CancellationToken cancellationToken,
        ILogger? logger = null,
        TerminalOutputService? terminalOutput = null,
        bool enableHeartbeat = false)
    {
        // Ensure redirects are configured
        startInfo.RedirectStandardOutput = true;
        startInfo.RedirectStandardError = true;
        startInfo.UseShellExecute = false;
        startInfo.CreateNoWindow = true;

        using var process = new Process { StartInfo = startInfo };

        var output = new StringBuilder(512);
        var error = new StringBuilder(256);
        var stdoutLineCount = 0;
        var stderrLineCount = 0;
        var outputTruncated = false;

        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data == null || outputTruncated) return;

            Interlocked.Increment(ref stdoutLineCount);
            if (output.Length + e.Data.Length > MaxOutputSize)
            {
                outputTruncated = true;
                logger?.LogWarning("CLI output exceeded {MaxSize} bytes, truncating", MaxOutputSize);
            }
            else
            {
                output.AppendLine(e.Data);
                terminalOutput?.Push(sourceName, "stdout", e.Data);
            }
        };

        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data == null) return;

            Interlocked.Increment(ref stderrLineCount);
            error.AppendLine(e.Data);
            terminalOutput?.Push(sourceName, "stderr", e.Data);
        };

        if (!process.Start())
        {
            throw new InvalidOperationException($"Failed to start process: {startInfo.FileName}");
        }

        terminalOutput?.Push(sourceName, "info",
            $"Started {startInfo.FileName} (PID: {process.Id}, timeout: {timeoutMs}ms)");

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(timeoutMs);

        // Optional heartbeat timer for long-running processes
        IDisposable? heartbeatDisposable = null;
        Stopwatch? heartbeatSw = null;
        if (enableHeartbeat && terminalOutput != null)
        {
            heartbeatSw = Stopwatch.StartNew();
            var heartbeatProcess = process;
            heartbeatDisposable = new Timer(_ =>
            {
                try
                {
                    var elapsed = heartbeatSw.Elapsed;
                    var alive = !heartbeatProcess.HasExited;
                    var outCount = Volatile.Read(ref stdoutLineCount);
                    var errCount = Volatile.Read(ref stderrLineCount);
                    terminalOutput.Push(sourceName, "info",
                        $"Still waiting... ({elapsed.TotalSeconds:F0}s elapsed, PID {heartbeatProcess.Id} " +
                        $"{(alive ? "running" : "EXITED")}, stdout: {outCount} lines, stderr: {errCount} lines)");
                }
                catch { /* process may be disposed */ }
            }, null, 10_000, 10_000);
        }

        try
        {
            await process.WaitForExitAsync(cts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            heartbeatSw?.Stop();
            var wasCallerCancellation = cancellationToken.IsCancellationRequested;

            terminalOutput?.Push(sourceName, "stderr",
                wasCallerCancellation
                    ? "Request was cancelled by caller"
                    : $"Timed out after {timeoutMs}ms -- killing process PID {process.Id}");

            KillProcess(process, logger);

            if (wasCallerCancellation)
                throw new OperationCanceledException($"{sourceName} request cancelled", cancellationToken);

            throw new TimeoutException($"Command timed out after {timeoutMs}ms");
        }
        finally
        {
            heartbeatDisposable?.Dispose();
        }

        // Cancel async readers and ensure all output handlers complete
        process.CancelOutputRead();
        process.CancelErrorRead();
        process.WaitForExit();

        if (process.ExitCode != 0)
        {
            var errorMessage = error.ToString().Trim();
            if (string.IsNullOrEmpty(errorMessage))
                errorMessage = $"Process exited with code {process.ExitCode}";

            terminalOutput?.Push(sourceName, "stderr",
                $"Process exited with code {process.ExitCode}: {errorMessage}");
            throw new InvalidOperationException(
                $"Command failed with exit code {process.ExitCode}: {errorMessage}");
        }

        terminalOutput?.Push(sourceName, "info",
            $"{startInfo.FileName} completed (exit code 0)");

        return new CliProcessResult
        {
            Output = output.ToString(),
            Error = error.ToString(),
            ExitCode = process.ExitCode
        };
    }

    private static void KillProcess(Process process, ILogger? logger)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(true);
                Thread.Sleep(100);
            }
        }
        catch (Exception ex)
        {
            logger?.LogError(ex, "Failed to kill hung process");
        }
    }
}
