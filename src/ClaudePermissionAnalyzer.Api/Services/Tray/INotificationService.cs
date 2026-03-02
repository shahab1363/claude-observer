using ClaudePermissionAnalyzer.Api.Models;

namespace ClaudePermissionAnalyzer.Api.Services.Tray;

/// <summary>
/// Shows native OS notifications (passive alerts and interactive approve/deny dialogs).
/// </summary>
public interface INotificationService
{
    /// <summary>Whether the platform supports interactive approve/deny dialogs.</summary>
    bool SupportsInteractive { get; }

    /// <summary>Show a passive notification (no user action required).</summary>
    Task ShowAlertAsync(NotificationInfo info);

    /// <summary>
    /// Show an interactive dialog with Approve/Deny buttons.
    /// Returns the user's choice, or null on timeout/error.
    /// </summary>
    Task<TrayDecision?> ShowInteractiveAsync(NotificationInfo info, TimeSpan timeout);
}
