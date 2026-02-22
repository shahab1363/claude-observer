using ClaudePermissionAnalyzer.Api.Models;
using ClaudePermissionAnalyzer.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace ClaudePermissionAnalyzer.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class DashboardController : ControllerBase
{
    private readonly SessionManager _sessionManager;
    private readonly ILogger<DashboardController> _logger;

    public DashboardController(
        SessionManager sessionManager,
        ILogger<DashboardController> logger)
    {
        _sessionManager = sessionManager;
        _logger = logger;
    }

    [HttpGet("stats")]
    public async Task<IActionResult> GetStats(CancellationToken cancellationToken)
    {
        try
        {
            var sessions = await _sessionManager.GetAllSessionsAsync(cancellationToken);
            var today = DateTime.UtcNow.Date;

            var allEvents = sessions.SelectMany(s => s.ConversationHistory).ToList();
            var todayEvents = allEvents.Where(e => e.Timestamp.Date == today).ToList();

            var autoApprovedToday = todayEvents.Count(e => e.Decision == "auto-approved");
            var deniedToday = todayEvents.Count(e => e.Decision == "denied");
            var activeSessions = sessions.Count(s => s.LastActivity > DateTime.UtcNow.AddHours(-1));
            var scoredEvents = todayEvents.Where(e => e.SafetyScore.HasValue).ToList();
            var avgScore = scoredEvents.Count > 0
                ? (int)Math.Round(scoredEvents.Average(e => e.SafetyScore!.Value))
                : 0;

            return Ok(new
            {
                autoApprovedToday,
                deniedToday,
                activeSessions,
                avgSafetyScore = avgScore,
                totalEventsToday = todayEvents.Count
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to retrieve dashboard stats");
            return StatusCode(500, new { error = "Failed to load dashboard statistics" });
        }
    }

    [HttpGet("sessions")]
    public async Task<IActionResult> GetSessions(CancellationToken cancellationToken)
    {
        try
        {
            var sessions = await _sessionManager.GetAllSessionsAsync(cancellationToken);

            var result = sessions
                .OrderByDescending(s => s.LastActivity)
                .Select(s => new
                {
                    s.SessionId,
                    s.StartTime,
                    s.LastActivity,
                    s.WorkingDirectory,
                    eventCount = s.ConversationHistory.Count,
                    approvedCount = s.ConversationHistory.Count(e => e.Decision == "auto-approved"),
                    deniedCount = s.ConversationHistory.Count(e => e.Decision == "denied"),
                    lastTool = s.ConversationHistory.LastOrDefault()?.ToolName
                })
                .ToList();

            return Ok(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to retrieve sessions");
            return StatusCode(500, new { error = "Failed to load sessions" });
        }
    }

    [HttpGet("activity")]
    public async Task<IActionResult> GetRecentActivity(
        [FromQuery] int limit = 50,
        CancellationToken cancellationToken = default)
    {
        try
        {
            limit = Math.Clamp(limit, 1, 200);
            var sessions = await _sessionManager.GetAllSessionsAsync(cancellationToken);

            var recentEvents = sessions
                .SelectMany(s => s.ConversationHistory.Select(e => new
                {
                    e.Timestamp,
                    e.Type,
                    e.ToolName,
                    e.ToolInput,
                    e.Decision,
                    e.SafetyScore,
                    e.Reasoning,
                    e.Category,
                    e.Content,
                    e.HandlerName,
                    e.PromptTemplate,
                    e.Threshold,
                    SessionId = s.SessionId
                }))
                .OrderByDescending(e => e.Timestamp)
                .Take(limit)
                .ToList();

            return Ok(recentEvents);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to retrieve recent activity");
            return StatusCode(500, new { error = "Failed to load activity feed" });
        }
    }

    [HttpGet("trends")]
    public async Task<IActionResult> GetTrends(
        [FromQuery] int days = 7,
        CancellationToken cancellationToken = default)
    {
        try
        {
            days = Math.Clamp(days, 1, 30);
            var sessions = await _sessionManager.GetAllSessionsAsync(cancellationToken);
            var allEvents = sessions.SelectMany(s => s.ConversationHistory).ToList();

            var trends = Enumerable.Range(0, days)
                .Select(i => DateTime.UtcNow.Date.AddDays(-i))
                .Select(date =>
                {
                    var dayEvents = allEvents.Where(e => e.Timestamp.Date == date).ToList();
                    return new
                    {
                        date = date.ToString("yyyy-MM-dd"),
                        approved = dayEvents.Count(e => e.Decision == "auto-approved"),
                        denied = dayEvents.Count(e => e.Decision == "denied"),
                        total = dayEvents.Count
                    };
                })
                .Reverse()
                .ToList();

            return Ok(trends);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to retrieve trends data");
            return StatusCode(500, new { error = "Failed to load trends" });
        }
    }

    [HttpGet("health")]
    public IActionResult GetHealth()
    {
        return Ok(new
        {
            status = "healthy",
            timestamp = DateTime.UtcNow,
            uptime = Environment.TickCount64
        });
    }
}
