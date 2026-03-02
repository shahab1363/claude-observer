using ClaudePermissionAnalyzer.Api.Models;
using ClaudePermissionAnalyzer.Api.Services.Tray;
using Microsoft.AspNetCore.Mvc;

namespace ClaudePermissionAnalyzer.Api.Controllers;

/// <summary>
/// Web dashboard fallback for pending tray decisions.
/// Allows approve/reject from the browser when native tray notifications aren't available.
/// </summary>
[ApiController]
[Route("api/tray")]
public class TrayController : ControllerBase
{
    private readonly PendingDecisionService _pendingService;
    private readonly ILogger<TrayController> _logger;

    public TrayController(PendingDecisionService pendingService, ILogger<TrayController> logger)
    {
        _pendingService = pendingService;
        _logger = logger;
    }

    /// <summary>
    /// Lists all currently pending interactive decisions.
    /// </summary>
    [HttpGet("pending")]
    public IActionResult GetPending()
    {
        var pending = _pendingService.GetPending();
        var result = pending.Select(p => new
        {
            id = p.Id,
            toolName = p.Info.ToolName,
            safetyScore = p.Info.SafetyScore,
            category = p.Info.Category,
            reasoning = p.Info.Reasoning,
            level = p.Info.Level.ToString().ToLowerInvariant(),
            createdAt = p.CreatedAt
        });

        return Ok(result);
    }

    /// <summary>
    /// Resolves a pending decision from the web dashboard.
    /// POST /api/tray/decide/{id} with body: { "approve": true }
    /// </summary>
    [HttpPost("decide/{id}")]
    public IActionResult Decide(string id, [FromBody] DecideRequest request)
    {
        var decision = request.Approve ? TrayDecision.Approve : TrayDecision.Deny;
        var resolved = _pendingService.TryResolve(id, decision);

        if (!resolved)
        {
            return NotFound(new { error = "Decision not found or already resolved" });
        }

        _logger.LogInformation("Web dashboard resolved decision {Id} with {Decision}", id, decision);
        return Ok(new { resolved = true, decision = decision.ToString().ToLowerInvariant() });
    }

    public class DecideRequest
    {
        public bool Approve { get; set; }
    }
}
