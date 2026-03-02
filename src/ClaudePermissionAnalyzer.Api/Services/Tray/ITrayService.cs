namespace ClaudePermissionAnalyzer.Api.Services.Tray;

/// <summary>
/// Manages the system tray icon (Windows) or equivalent presence indicator.
/// </summary>
public interface ITrayService : IDisposable
{
    /// <summary>Whether the tray service is available on this platform.</summary>
    bool IsAvailable { get; }

    /// <summary>Start the tray icon/service.</summary>
    Task StartAsync();

    /// <summary>Update the tray icon tooltip/status text.</summary>
    void UpdateStatus(string status);
}
