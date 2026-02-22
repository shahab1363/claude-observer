using ClaudePermissionAnalyzer.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace ClaudePermissionAnalyzer.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AdaptiveThresholdController : ControllerBase
{
    private readonly AdaptiveThresholdService _adaptiveService;
    private readonly ILogger<AdaptiveThresholdController> _logger;

    public AdaptiveThresholdController(AdaptiveThresholdService adaptiveService, ILogger<AdaptiveThresholdController> logger)
    {
        _adaptiveService = adaptiveService;
        _logger = logger;
    }

    [HttpGet("stats")]
    public IActionResult GetStats()
    {
        var stats = _adaptiveService.GetToolStats();
        return Ok(new
        {
            toolStats = stats.Select(s => new
            {
                toolName = s.Key,
                totalDecisions = s.Value.TotalDecisions,
                overrideCount = s.Value.OverrideCount,
                falsePositives = s.Value.FalsePositives,
                falseNegatives = s.Value.FalseNegatives,
                suggestedThreshold = s.Value.SuggestedThreshold,
                averageSafetyScore = Math.Round(s.Value.AverageSafetyScore, 1),
                confidenceLevel = Math.Round(s.Value.ConfidenceLevel, 2)
            })
        });
    }

    [HttpGet("overrides")]
    public IActionResult GetRecentOverrides([FromQuery] int count = 20)
    {
        var overrides = _adaptiveService.GetRecentOverrides(Math.Min(count, 100));
        return Ok(new { overrides });
    }

    [HttpPost("override")]
    public async Task<IActionResult> RecordOverride([FromBody] RecordOverrideRequest request)
    {
        if (string.IsNullOrWhiteSpace(request?.ToolName))
        {
            return BadRequest(new { error = "toolName is required" });
        }

        if (string.IsNullOrWhiteSpace(request.OriginalDecision) || string.IsNullOrWhiteSpace(request.UserAction))
        {
            return BadRequest(new { error = "originalDecision and userAction are required" });
        }

        await _adaptiveService.RecordOverrideAsync(
            request.ToolName,
            request.OriginalDecision,
            request.UserAction,
            request.SafetyScore,
            request.Threshold,
            request.SessionId ?? string.Empty);

        var suggested = _adaptiveService.GetSuggestedThreshold(request.ToolName);
        return Ok(new
        {
            recorded = true,
            suggestedThreshold = suggested
        });
    }

    [HttpGet("suggestion/{toolName}")]
    public IActionResult GetSuggestion(string toolName)
    {
        var suggested = _adaptiveService.GetSuggestedThreshold(toolName);
        var stats = _adaptiveService.GetToolStats();
        stats.TryGetValue(toolName, out var toolStats);

        return Ok(new
        {
            toolName,
            suggestedThreshold = suggested,
            confidence = toolStats?.ConfidenceLevel ?? 0,
            totalDecisions = toolStats?.TotalDecisions ?? 0,
            overrideCount = toolStats?.OverrideCount ?? 0
        });
    }
}

public class RecordOverrideRequest
{
    public string ToolName { get; set; } = string.Empty;
    public string OriginalDecision { get; set; } = string.Empty;
    public string UserAction { get; set; } = string.Empty;
    public int SafetyScore { get; set; }
    public int Threshold { get; set; }
    public string? SessionId { get; set; }
}
