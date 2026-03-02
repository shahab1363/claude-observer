using System.Diagnostics;

namespace Leash.Api.Services.Tray.Linux;

/// <summary>
/// Linux tray service using yad --notification as a background subprocess (if available).
/// Falls back to IsAvailable = false if yad is not installed.
/// </summary>
public class LinuxTrayService : ITrayService
{
    private readonly ILogger<LinuxTrayService> _logger;
    private Process? _yadProcess;
    private bool _available;

    public LinuxTrayService(ILogger<LinuxTrayService> logger)
    {
        _logger = logger;
    }

    public bool IsAvailable => _available;

    public async Task StartAsync()
    {
        // Check if yad is available
        if (!await IsCommandAvailable("yad"))
        {
            _logger.LogInformation("yad not found — Linux tray icon not available. Notifications will use notify-send/zenity.");
            return;
        }

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "yad",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardInput = true
            };
            psi.ArgumentList.Add("--notification");
            psi.ArgumentList.Add("--text=Leash");
            psi.ArgumentList.Add("--image=dialog-information");

            _yadProcess = new Process { StartInfo = psi };
            _yadProcess.Start();
            _available = true;
            _logger.LogInformation("Linux tray icon started via yad (PID: {Pid})", _yadProcess.Id);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to start yad notification icon");
        }
    }

    public void UpdateStatus(string status)
    {
        // yad --notification doesn't easily support tooltip updates without pipes
    }

    public void Dispose()
    {
        try
        {
            if (_yadProcess != null && !_yadProcess.HasExited)
            {
                _yadProcess.Kill(true);
                _yadProcess.Dispose();
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Error disposing Linux tray service");
        }
    }

    private static async Task<bool> IsCommandAvailable(string command)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "which",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                CreateNoWindow = true
            };
            psi.ArgumentList.Add(command);

            using var p = Process.Start(psi);
            if (p == null) return false;
            await p.WaitForExitAsync();
            return p.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }
}
