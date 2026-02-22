using ClaudePermissionAnalyzer.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace ClaudePermissionAnalyzer.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class InsightsController : ControllerBase
{
    private readonly InsightsEngine _insightsEngine;
    private readonly ILogger<InsightsController> _logger;

    public InsightsController(InsightsEngine insightsEngine, ILogger<InsightsController> logger)
    {
        _insightsEngine = insightsEngine;
        _logger = logger;
    }

    [HttpGet]
    public IActionResult GetInsights([FromQuery] bool includeAll = false)
    {
        var insights = _insightsEngine.GetInsights(includeAll);
        return Ok(new
        {
            insights,
            count = insights.Count,
            generatedAt = DateTime.UtcNow
        });
    }

    [HttpPost("dismiss/{insightId}")]
    public IActionResult DismissInsight(string insightId)
    {
        _insightsEngine.DismissInsight(insightId);
        return Ok(new { dismissed = true });
    }

    [HttpPost("regenerate")]
    public IActionResult Regenerate()
    {
        _insightsEngine.RegenerateInsights();
        var insights = _insightsEngine.GetInsights();
        return Ok(new
        {
            insights,
            count = insights.Count,
            generatedAt = DateTime.UtcNow
        });
    }
}
