using ClaudePermissionAnalyzer.Api.Models;
using ClaudePermissionAnalyzer.Api.Services;
using ClaudePermissionAnalyzer.Api.Exceptions;
using Microsoft.AspNetCore.Mvc;

namespace ClaudePermissionAnalyzer.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ConfigController : ControllerBase
{
    private readonly ClaudePermissionAnalyzer.Api.Services.ConfigurationManager _configManager;
    private readonly ILogger<ConfigController> _logger;

    public ConfigController(
        ClaudePermissionAnalyzer.Api.Services.ConfigurationManager configManager,
        ILogger<ConfigController> logger)
    {
        _configManager = configManager;
        _logger = logger;
    }

    [HttpGet]
    public async Task<IActionResult> GetConfig(CancellationToken cancellationToken)
    {
        try
        {
            var config = await _configManager.LoadAsync();
            return Ok(config);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load configuration");
            return StatusCode(500, new { error = "Failed to load configuration" });
        }
    }

    [HttpPut]
    public async Task<IActionResult> UpdateConfig(
        [FromBody] Configuration config,
        CancellationToken cancellationToken)
    {
        if (config == null)
        {
            return BadRequest(new { error = "Configuration body is required" });
        }

        try
        {
            await _configManager.UpdateAsync(config);
            _logger.LogInformation("Configuration updated via API");
            return Ok(new { message = "Configuration updated successfully" });
        }
        catch (ConfigurationException ex)
        {
            _logger.LogError(ex, "Failed to save configuration");
            return StatusCode(500, new { error = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error updating configuration");
            return StatusCode(500, new { error = "Failed to update configuration" });
        }
    }

    [HttpGet("handlers/{hookEventName}")]
    public IActionResult GetHandlers(string hookEventName)
    {
        if (string.IsNullOrWhiteSpace(hookEventName))
            return BadRequest(new { error = "Hook event name is required" });

        var handlers = _configManager.GetHandlersForHook(hookEventName);
        return Ok(handlers);
    }
}
