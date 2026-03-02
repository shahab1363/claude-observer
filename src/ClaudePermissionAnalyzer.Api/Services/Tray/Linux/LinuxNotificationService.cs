using System.Diagnostics;
using ClaudePermissionAnalyzer.Api.Models;

namespace ClaudePermissionAnalyzer.Api.Services.Tray.Linux;

/// <summary>
/// Linux notification service using notify-send for passive alerts
/// and zenity for interactive approve/deny dialogs.
/// </summary>
public class LinuxNotificationService : INotificationService
{
    private readonly ILogger<LinuxNotificationService> _logger;
    private bool? _zenityAvailable;

    public LinuxNotificationService(ILogger<LinuxNotificationService> logger)
    {
        _logger = logger;
    }

    public bool SupportsInteractive => CheckZenityAvailable();

    public async Task ShowAlertAsync(NotificationInfo info)
    {
        try
        {
            var urgency = info.Level switch
            {
                NotificationLevel.Danger => "critical",
                NotificationLevel.Warning => "normal",
                _ => "low"
            };

            var psi = new ProcessStartInfo
            {
                FileName = "notify-send",
                UseShellExecute = false,
                CreateNoWindow = true
            };
            psi.ArgumentList.Add($"--urgency={urgency}");
            psi.ArgumentList.Add("--app-name=Claude Observer");
            psi.ArgumentList.Add(info.Title);
            psi.ArgumentList.Add(info.Body);

            using var process = Process.Start(psi);
            if (process != null)
            {
                using var cts = new CancellationTokenSource(5000);
                await process.WaitForExitAsync(cts.Token);
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to show Linux notification via notify-send");
        }
    }

    public async Task<TrayDecision?> ShowInteractiveAsync(NotificationInfo info, TimeSpan timeout)
    {
        if (!CheckZenityAvailable()) return null;

        try
        {
            var bodyParts = new List<string>();
            if (!string.IsNullOrEmpty(info.ToolName))
                bodyParts.Add($"Tool: {info.ToolName}");
            if (info.SafetyScore.HasValue)
                bodyParts.Add($"Score: {info.SafetyScore}");
            if (!string.IsNullOrEmpty(info.Reasoning))
            {
                var reasoning = info.Reasoning.Length > 200 ? info.Reasoning[..197] + "..." : info.Reasoning;
                bodyParts.Add(reasoning);
            }

            var text = string.Join("\n", bodyParts);
            var timeoutSec = (int)timeout.TotalSeconds;

            var psi = new ProcessStartInfo
            {
                FileName = "zenity",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true
            };
            psi.ArgumentList.Add("--question");
            psi.ArgumentList.Add($"--title={info.Title}");
            psi.ArgumentList.Add($"--text={text}");
            psi.ArgumentList.Add("--ok-label=Approve");
            psi.ArgumentList.Add("--cancel-label=Deny");
            psi.ArgumentList.Add($"--timeout={timeoutSec}");

            using var process = Process.Start(psi);
            if (process == null) return null;

            using var cts = new CancellationTokenSource((int)timeout.TotalMilliseconds + 5000);
            try
            {
                await process.WaitForExitAsync(cts.Token);
            }
            catch (OperationCanceledException)
            {
                try { if (!process.HasExited) process.Kill(true); } catch { }
                return null;
            }

            // zenity exit codes: 0 = OK (Approve), 1 = Cancel (Deny), 5 = Timeout
            return process.ExitCode switch
            {
                0 => TrayDecision.Approve,
                1 => TrayDecision.Deny,
                5 => null, // timeout
                _ => null
            };
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to show interactive Linux dialog via zenity");
            return null;
        }
    }

    private bool CheckZenityAvailable()
    {
        if (_zenityAvailable.HasValue) return _zenityAvailable.Value;

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "which",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                CreateNoWindow = true
            };
            psi.ArgumentList.Add("zenity");

            using var p = Process.Start(psi);
            p?.WaitForExit(3000);
            _zenityAvailable = p?.ExitCode == 0;
        }
        catch
        {
            _zenityAvailable = false;
        }

        return _zenityAvailable.Value;
    }
}
