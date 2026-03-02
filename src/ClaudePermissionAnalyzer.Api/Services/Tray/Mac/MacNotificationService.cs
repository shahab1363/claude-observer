using System.Diagnostics;
using ClaudePermissionAnalyzer.Api.Models;

namespace ClaudePermissionAnalyzer.Api.Services.Tray.Mac;

/// <summary>
/// macOS notification service using osascript (AppleScript).
/// Passive: display notification. Interactive: display dialog with buttons.
/// </summary>
public class MacNotificationService : INotificationService
{
    private readonly ILogger<MacNotificationService> _logger;

    public MacNotificationService(ILogger<MacNotificationService> logger)
    {
        _logger = logger;
    }

    public bool SupportsInteractive => true;

    public async Task ShowAlertAsync(NotificationInfo info)
    {
        try
        {
            var body = EscapeAppleScript(info.Body);
            var title = EscapeAppleScript(info.Title);
            var script = $"display notification \"{body}\" with title \"{title}\"";

            await RunOsascriptAsync(script, timeoutMs: 5000);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to show macOS notification");
        }
    }

    public async Task<TrayDecision?> ShowInteractiveAsync(NotificationInfo info, TimeSpan timeout)
    {
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
                bodyParts.Add($"\n{reasoning}");
            }

            var body = EscapeAppleScript(string.Join("   ", bodyParts));
            var title = EscapeAppleScript(info.Title);
            var givingUp = (int)timeout.TotalSeconds;

            var script = $"display dialog \"{body}\" with title \"{title}\" buttons {{\"Deny\",\"Approve\"}} default button \"Approve\" giving up after {givingUp}";

            var result = await RunOsascriptAsync(script, (int)timeout.TotalMilliseconds + 5000);

            if (result == null)
                return null;

            // osascript returns "button returned:Approve, gave up:false"
            if (result.Contains("button returned:Approve", StringComparison.OrdinalIgnoreCase))
                return TrayDecision.Approve;
            if (result.Contains("button returned:Deny", StringComparison.OrdinalIgnoreCase))
                return TrayDecision.Deny;
            if (result.Contains("gave up:true", StringComparison.OrdinalIgnoreCase))
                return null; // timeout

            return null;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to show interactive macOS dialog");
            return null;
        }
    }

    private static async Task<string?> RunOsascriptAsync(string script, int timeoutMs)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "osascript",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        psi.ArgumentList.Add("-e");
        psi.ArgumentList.Add(script);

        using var process = new Process { StartInfo = psi };
        if (!process.Start()) return null;

        using var cts = new CancellationTokenSource(timeoutMs);
        try
        {
            await process.WaitForExitAsync(cts.Token);
            return await process.StandardOutput.ReadToEndAsync();
        }
        catch (OperationCanceledException)
        {
            try { if (!process.HasExited) process.Kill(true); } catch { }
            return null;
        }
    }

    private static string EscapeAppleScript(string s)
        => s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "");
}
