namespace ClaudePermissionAnalyzer.Api.Services.Tray;

/// <summary>
/// No-op tray service used when tray is disabled or unsupported on the current platform.
/// </summary>
public class NullTrayService : ITrayService
{
    public bool IsAvailable => false;

    public Task StartAsync() => Task.CompletedTask;

    public void UpdateStatus(string status) { }

    public void Dispose() { }
}
