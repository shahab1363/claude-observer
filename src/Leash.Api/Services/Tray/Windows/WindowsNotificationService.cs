#if WINDOWS
using System.Runtime.Versioning;
using Leash.Api.Models;

namespace Leash.Api.Services.Tray.Windows;

/// <summary>
/// Windows notification service using NotifyIcon balloon tips for passive alerts
/// and a small WinForms popup for interactive approve/deny.
/// </summary>
[SupportedOSPlatform("windows")]
public class WindowsNotificationService : INotificationService
{
    private readonly WindowsTrayService _trayService;
    private readonly PendingDecisionService _pendingService;
    private readonly ILogger<WindowsNotificationService> _logger;

    public WindowsNotificationService(
        WindowsTrayService trayService,
        PendingDecisionService pendingService,
        ILogger<WindowsNotificationService> logger)
    {
        _trayService = trayService;
        _pendingService = pendingService;
        _logger = logger;
    }

    public bool SupportsInteractive => _trayService.IsAvailable;

    public Task ShowAlertAsync(NotificationInfo info)
    {
        if (!_trayService.IsAvailable) return Task.CompletedTask;

        var icon = info.Level switch
        {
            NotificationLevel.Danger => System.Windows.Forms.ToolTipIcon.Error,
            NotificationLevel.Warning => System.Windows.Forms.ToolTipIcon.Warning,
            _ => System.Windows.Forms.ToolTipIcon.Info
        };

        _trayService.ShowBalloonTip(info.Title, info.Body, icon);
        return Task.CompletedTask;
    }

    public async Task<TrayDecision?> ShowInteractiveAsync(NotificationInfo info, TimeSpan timeout)
    {
        if (!_trayService.IsAvailable) return null;

        try
        {
            var tcs = new TaskCompletionSource<TrayDecision?>(TaskCreationOptions.RunContinuationsAsynchronously);
            using var cts = new CancellationTokenSource(timeout);
            cts.Token.Register(() => tcs.TrySetResult(null));

            _trayService.InvokeOnStaThread(() =>
            {
                try
                {
                    var form = new TrayDecisionForm(info, (int)timeout.TotalSeconds);
                    form.DecisionMade += (_, decision) =>
                    {
                        tcs.TrySetResult(decision);
                    };
                    form.Show();
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Failed to show interactive form");
                    tcs.TrySetResult(null);
                }
            });

            return await tcs.Task;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Interactive notification failed");
            return null;
        }
    }
}
#endif
