using ClaudePermissionAnalyzer.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace ClaudePermissionAnalyzer.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class HooksController : ControllerBase
{
    private readonly HookInstaller _hookInstaller;
    private readonly EnforcementService _enforcementService;
    private readonly ILogger<HooksController> _logger;

    public HooksController(
        HookInstaller hookInstaller,
        EnforcementService enforcementService,
        ILogger<HooksController> logger)
    {
        _hookInstaller = hookInstaller;
        _enforcementService = enforcementService;
        _logger = logger;
    }

    [HttpGet("status")]
    public IActionResult GetStatus()
    {
        return Ok(new
        {
            installed = _hookInstaller.IsInstalled(),
            enforced = _enforcementService.IsEnforced
        });
    }

    [HttpPost("enforce")]
    public async Task<IActionResult> ToggleEnforcement()
    {
        await _enforcementService.ToggleAsync();
        _logger.LogInformation("Enforcement toggled to {State}", _enforcementService.IsEnforced);
        return Ok(new
        {
            enforced = _enforcementService.IsEnforced,
            message = _enforcementService.IsEnforced
                ? "Enforcement enabled - hooks will return approve/deny decisions"
                : "Observe-only mode - hooks will log but not decide"
        });
    }

    [HttpPost("install")]
    public IActionResult InstallHooks()
    {
        try
        {
            _hookInstaller.Install();
            return Ok(new { installed = true, message = "Hooks installed successfully" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to install hooks");
            return StatusCode(500, new { error = "Failed to install hooks: " + ex.Message });
        }
    }

    [HttpPost("uninstall")]
    public IActionResult UninstallHooks()
    {
        try
        {
            _hookInstaller.Uninstall();
            return Ok(new { installed = false, message = "Hooks uninstalled successfully" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to uninstall hooks");
            return StatusCode(500, new { error = "Failed to uninstall hooks: " + ex.Message });
        }
    }
}
