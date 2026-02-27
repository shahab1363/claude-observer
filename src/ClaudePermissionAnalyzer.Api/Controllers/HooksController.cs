using ClaudePermissionAnalyzer.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace ClaudePermissionAnalyzer.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class HooksController : ControllerBase
{
    private readonly HookInstaller _hookInstaller;
    private readonly CopilotHookInstaller _copilotHookInstaller;
    private readonly EnforcementService _enforcementService;
    private readonly ILogger<HooksController> _logger;

    public HooksController(
        HookInstaller hookInstaller,
        CopilotHookInstaller copilotHookInstaller,
        EnforcementService enforcementService,
        ILogger<HooksController> logger)
    {
        _hookInstaller = hookInstaller;
        _copilotHookInstaller = copilotHookInstaller;
        _enforcementService = enforcementService;
        _logger = logger;
    }

    [HttpGet("status")]
    public IActionResult GetStatus()
    {
        return Ok(new
        {
            installed = _hookInstaller.IsInstalled(),
            enforced = _enforcementService.IsEnforced,
            enforcementMode = _enforcementService.Mode,
            copilot = new
            {
                userInstalled = _copilotHookInstaller.IsUserInstalled()
            }
        });
    }

    [HttpPost("enforce")]
    public async Task<IActionResult> ToggleEnforcement([FromQuery] string? mode = null)
    {
        if (!string.IsNullOrEmpty(mode))
        {
            if (!EnforcementService.ValidModes.Contains(mode))
                return BadRequest(new { error = $"Invalid mode: {mode}. Valid: {string.Join(", ", EnforcementService.ValidModes)}" });
            await _enforcementService.SetModeAsync(mode);
        }
        else
        {
            await _enforcementService.CycleModeAsync();
        }

        var currentMode = _enforcementService.Mode;
        _logger.LogInformation("Enforcement mode set to {Mode}", currentMode);

        var message = currentMode switch
        {
            "observe" => "Observe-only mode - hooks log but do not decide",
            "approve-only" => "Approve-only mode - safe requests auto-approved, uncertain ones fall through to user",
            "enforce" => "Full enforcement - hooks return approve/deny decisions",
            _ => $"Mode: {currentMode}"
        };

        return Ok(new
        {
            enforced = _enforcementService.IsEnforced,
            enforcementMode = currentMode,
            message
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

    // ---- Copilot Hook Endpoints ----

    [HttpPost("copilot/install")]
    public IActionResult InstallCopilotHooks([FromQuery] string level = "user", [FromQuery] string? repoPath = null)
    {
        try
        {
            if (string.Equals(level, "repo", StringComparison.OrdinalIgnoreCase))
            {
                if (string.IsNullOrWhiteSpace(repoPath))
                    return BadRequest(new { error = "repoPath query parameter is required for repo-level installation" });

                if (!Directory.Exists(repoPath))
                    return BadRequest(new { error = $"Repository path does not exist: {repoPath}" });

                _copilotHookInstaller.InstallRepo(repoPath);
                return Ok(new { installed = true, level = "repo", message = "Copilot hooks installed at repo level" });
            }
            else
            {
                _copilotHookInstaller.InstallUser();
                return Ok(new { installed = true, level = "user", message = "Copilot hooks installed at user level" });
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to install Copilot hooks");
            return StatusCode(500, new { error = "Failed to install Copilot hooks: " + ex.Message });
        }
    }

    [HttpPost("copilot/uninstall")]
    public IActionResult UninstallCopilotHooks([FromQuery] string level = "user", [FromQuery] string? repoPath = null)
    {
        try
        {
            if (string.Equals(level, "repo", StringComparison.OrdinalIgnoreCase))
            {
                if (string.IsNullOrWhiteSpace(repoPath))
                    return BadRequest(new { error = "repoPath query parameter is required for repo-level uninstall" });

                _copilotHookInstaller.UninstallRepo(repoPath);
                return Ok(new { installed = false, level = "repo", message = "Copilot hooks uninstalled from repo level" });
            }
            else
            {
                _copilotHookInstaller.UninstallUser();
                return Ok(new { installed = false, level = "user", message = "Copilot hooks uninstalled from user level" });
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to uninstall Copilot hooks");
            return StatusCode(500, new { error = "Failed to uninstall Copilot hooks: " + ex.Message });
        }
    }
}
