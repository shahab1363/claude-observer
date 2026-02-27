using ClaudePermissionAnalyzer.Api.Models;
using Microsoft.Extensions.Logging;

namespace ClaudePermissionAnalyzer.Api.Services;

public class ProfileService
{
    private readonly ConfigurationManager _configManager;
    private readonly ILogger<ProfileService>? _logger;
    private PermissionProfile _activeProfile;
    private string _activeProfileKey;

    public ProfileService(ConfigurationManager configManager, ILogger<ProfileService>? logger = null)
    {
        _configManager = configManager;
        _logger = logger;
        _activeProfileKey = "moderate";
        _activeProfile = PermissionProfile.BuiltInProfiles["moderate"];
    }

    public async Task InitializeAsync()
    {
        var config = await _configManager.LoadAsync();
        var key = config.Profiles.ActiveProfile;
        if (TryGetProfile(key, config.Profiles, out var profile))
        {
            _activeProfileKey = key;
            _activeProfile = profile;
            _logger?.LogDebug("Loaded permission profile: {Profile}", key);
        }
    }

    public PermissionProfile GetActiveProfile() => _activeProfile;

    public string GetActiveProfileKey() => _activeProfileKey;

    public virtual int GetThresholdForTool(string? toolName)
    {
        if (!string.IsNullOrEmpty(toolName) &&
            _activeProfile.ThresholdOverrides.TryGetValue(toolName, out var toolThreshold))
        {
            return toolThreshold;
        }
        return _activeProfile.DefaultThreshold;
    }

    public virtual bool IsAutoApproveEnabled() => _activeProfile.AutoApproveEnabled;

    public async Task<bool> SwitchProfileAsync(string profileKey)
    {
        var config = await _configManager.LoadAsync();
        if (!TryGetProfile(profileKey, config.Profiles, out var profile))
        {
            _logger?.LogWarning("Profile not found: {Profile}", profileKey);
            return false;
        }

        _activeProfileKey = profileKey;
        _activeProfile = profile;
        config.Profiles.ActiveProfile = profileKey;
        await _configManager.SaveAsync();

        _logger?.LogDebug("Switched to permission profile: {Profile}", profileKey);
        return true;
    }

    public Dictionary<string, PermissionProfile> GetAllProfiles()
    {
        var all = new Dictionary<string, PermissionProfile>(PermissionProfile.BuiltInProfiles);
        // Custom profiles could be loaded from config here
        return all;
    }

    private static bool TryGetProfile(string key, ProfileConfig profileConfig, out PermissionProfile profile)
    {
        if (PermissionProfile.BuiltInProfiles.TryGetValue(key, out profile!))
            return true;
        if (profileConfig.CustomProfiles.TryGetValue(key, out profile!))
            return true;
        profile = PermissionProfile.BuiltInProfiles["moderate"];
        return false;
    }
}
