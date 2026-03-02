namespace ClaudePermissionAnalyzer.Api.Services.Tray.Mac;

/// <summary>
/// macOS tray service stub. A true menubar icon requires a Cocoa application bundle,
/// which isn't feasible from a .NET console app. Notifications work via osascript.
/// </summary>
public class MacTrayService : ITrayService
{
    private readonly ILogger<MacTrayService> _logger;

    public MacTrayService(ILogger<MacTrayService> logger)
    {
        _logger = logger;
    }

    public bool IsAvailable => false;

    public Task StartAsync()
    {
        _logger.LogInformation("macOS menubar icon not available (requires Cocoa bundle). Notifications will still work via osascript.");
        return Task.CompletedTask;
    }

    public void UpdateStatus(string status) { }

    public void Dispose() { }
}
