using ClaudePermissionAnalyzer.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace ClaudePermissionAnalyzer.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ProfileController : ControllerBase
{
    private readonly ProfileService _profileService;
    private readonly ILogger<ProfileController> _logger;

    public ProfileController(ProfileService profileService, ILogger<ProfileController> logger)
    {
        _profileService = profileService;
        _logger = logger;
    }

    [HttpGet]
    public IActionResult GetProfiles()
    {
        var profiles = _profileService.GetAllProfiles();
        var activeKey = _profileService.GetActiveProfileKey();

        return Ok(new
        {
            activeProfile = activeKey,
            profiles = profiles.Select(p => new
            {
                key = p.Key,
                name = p.Value.Name,
                description = p.Value.Description,
                defaultThreshold = p.Value.DefaultThreshold,
                autoApproveEnabled = p.Value.AutoApproveEnabled,
                thresholdOverrides = p.Value.ThresholdOverrides,
                isActive = p.Key == activeKey
            })
        });
    }

    [HttpPost("switch")]
    public async Task<IActionResult> SwitchProfile([FromBody] SwitchProfileRequest request)
    {
        if (string.IsNullOrWhiteSpace(request?.ProfileKey))
        {
            return BadRequest(new { error = "profileKey is required" });
        }

        var success = await _profileService.SwitchProfileAsync(request.ProfileKey);
        if (!success)
        {
            return NotFound(new { error = $"Profile '{request.ProfileKey}' not found" });
        }

        _logger.LogInformation("Profile switched to {Profile}", request.ProfileKey);
        return Ok(new
        {
            activeProfile = request.ProfileKey,
            profile = _profileService.GetActiveProfile()
        });
    }
}

public class SwitchProfileRequest
{
    public string ProfileKey { get; set; } = string.Empty;
}
