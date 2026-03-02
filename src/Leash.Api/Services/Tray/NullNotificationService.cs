using Leash.Api.Models;

namespace Leash.Api.Services.Tray;

/// <summary>
/// No-op notification service used when tray is disabled or unsupported.
/// ShowInteractiveAsync returns null (timeout behavior — Claude asks user normally).
/// </summary>
public class NullNotificationService : INotificationService
{
    public bool SupportsInteractive => false;

    public Task ShowAlertAsync(NotificationInfo info) => Task.CompletedTask;

    public Task<TrayDecision?> ShowInteractiveAsync(NotificationInfo info, TimeSpan timeout)
        => Task.FromResult<TrayDecision?>(null);
}
