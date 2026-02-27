namespace ClaudePermissionAnalyzer.Api.Services;

public class EnforcementService
{
    public static readonly string[] ValidModes = { "observe", "approve-only", "enforce" };

    private readonly ConfigurationManager _configManager;
    private readonly ILogger<EnforcementService> _logger;
    private string _mode;

    public EnforcementService(ConfigurationManager configManager, ILogger<EnforcementService> logger)
    {
        _configManager = configManager;
        _logger = logger;

        // Resolve initial mode: EnforcementMode takes precedence, fall back to bool
        var config = configManager.GetConfiguration();
        if (!string.IsNullOrEmpty(config.EnforcementMode) && ValidModes.Contains(config.EnforcementMode))
        {
            _mode = config.EnforcementMode;
        }
        else
        {
            _mode = config.EnforcementEnabled ? "enforce" : "observe";
        }
    }

    /// <summary>Current enforcement mode: "observe", "approve-only", or "enforce".</summary>
    public string Mode => _mode;

    /// <summary>Backward-compatible property. True when mode is "enforce".</summary>
    public bool IsEnforced => _mode == "enforce";

    public async Task SetModeAsync(string mode)
    {
        if (!ValidModes.Contains(mode))
            throw new ArgumentException($"Invalid enforcement mode: {mode}. Valid: {string.Join(", ", ValidModes)}");

        _mode = mode;
        var config = _configManager.GetConfiguration();
        config.EnforcementMode = mode;
        config.EnforcementEnabled = mode == "enforce"; // keep bool in sync
        await _configManager.UpdateAsync(config);
        _logger.LogInformation("Enforcement mode changed to {Mode}", mode);
    }

    /// <summary>Backward-compatible setter.</summary>
    public async Task SetEnforcedAsync(bool enforced)
    {
        await SetModeAsync(enforced ? "enforce" : "observe");
    }

    /// <summary>Cycle through modes: observe -> approve-only -> enforce -> observe.</summary>
    public async Task CycleModeAsync()
    {
        var nextMode = _mode switch
        {
            "observe" => "approve-only",
            "approve-only" => "enforce",
            "enforce" => "observe",
            _ => "observe"
        };
        await SetModeAsync(nextMode);
    }

    /// <summary>Backward-compatible toggle (now cycles 3 modes).</summary>
    public async Task ToggleAsync()
    {
        await CycleModeAsync();
    }
}
