namespace ClaudePermissionAnalyzer.Api.Services;

public class EnforcementService
{
    private readonly ConfigurationManager _configManager;
    private readonly ILogger<EnforcementService> _logger;
    private bool _isEnforced;

    public EnforcementService(ConfigurationManager configManager, ILogger<EnforcementService> logger)
    {
        _configManager = configManager;
        _logger = logger;
        _isEnforced = configManager.GetConfiguration().EnforcementEnabled;
    }

    public bool IsEnforced => _isEnforced;

    public async Task SetEnforcedAsync(bool enforced)
    {
        _isEnforced = enforced;
        var config = _configManager.GetConfiguration();
        config.EnforcementEnabled = enforced;
        await _configManager.UpdateAsync(config);
        _logger.LogInformation("Enforcement mode changed to {Mode}", enforced ? "enforced" : "observe-only");
    }

    public async Task ToggleAsync()
    {
        await SetEnforcedAsync(!_isEnforced);
    }
}
