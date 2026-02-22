using ClaudePermissionAnalyzer.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace ClaudePermissionAnalyzer.Api.Controllers;

[ApiController]
public class HealthController : ControllerBase
{
    private readonly SessionManager _sessionManager;

    public HealthController(SessionManager sessionManager)
    {
        _sessionManager = sessionManager;
    }

    [HttpGet("health")]
    [HttpGet("api/health")]
    public IActionResult GetHealth()
    {
        return Ok(new
        {
            status = "healthy",
            timestamp = DateTime.UtcNow,
            version = typeof(HealthController).Assembly.GetName().Version?.ToString() ?? "1.0.0"
        });
    }
}
