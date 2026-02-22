using ClaudePermissionAnalyzer.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace ClaudePermissionAnalyzer.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class QuickActionsController : ControllerBase
{
    private readonly ProfileService _profileService;
    private readonly InsightsEngine _insightsEngine;
    private readonly AdaptiveThresholdService _adaptiveService;
    private readonly ILogger<QuickActionsController> _logger;

    public QuickActionsController(
        ProfileService profileService,
        InsightsEngine insightsEngine,
        AdaptiveThresholdService adaptiveService,
        ILogger<QuickActionsController> logger)
    {
        _profileService = profileService;
        _insightsEngine = insightsEngine;
        _adaptiveService = adaptiveService;
        _logger = logger;
    }

    [HttpPost("lockdown")]
    public async Task<IActionResult> Lockdown()
    {
        await _profileService.SwitchProfileAsync("lockdown");
        _logger.LogWarning("Lockdown activated - all auto-approvals disabled");
        return Ok(new
        {
            action = "lockdown",
            message = "Lockdown activated. All operations now require manual approval.",
            profile = "lockdown"
        });
    }

    [HttpPost("trust-session")]
    public async Task<IActionResult> TrustSession()
    {
        await _profileService.SwitchProfileAsync("permissive");
        _logger.LogInformation("Trust session activated - switched to permissive profile");
        return Ok(new
        {
            action = "trust-session",
            message = "Session trusted. Switched to permissive mode with lower thresholds.",
            profile = "permissive"
        });
    }

    [HttpPost("reset")]
    public async Task<IActionResult> Reset()
    {
        await _profileService.SwitchProfileAsync("moderate");
        _logger.LogInformation("Reset to moderate profile");
        return Ok(new
        {
            action = "reset",
            message = "Reset to moderate profile with balanced thresholds.",
            profile = "moderate"
        });
    }

    [HttpGet("status")]
    public IActionResult GetStatus()
    {
        var profile = _profileService.GetActiveProfile();
        var profileKey = _profileService.GetActiveProfileKey();
        var insights = _insightsEngine.GetInsights();
        var stats = _adaptiveService.GetToolStats();

        return Ok(new
        {
            activeProfile = profileKey,
            profileName = profile.Name,
            autoApproveEnabled = profile.AutoApproveEnabled,
            defaultThreshold = profile.DefaultThreshold,
            pendingInsights = insights.Count,
            trackedTools = stats.Count,
            totalOverrides = stats.Values.Sum(s => s.OverrideCount)
        });
    }
}
