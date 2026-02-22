using ClaudePermissionAnalyzer.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace ClaudePermissionAnalyzer.Api.Controllers;

[ApiController]
[Route("api/sessions")]
public class SessionsController : ControllerBase
{
    private readonly SessionManager _sessionManager;
    private readonly ILogger<SessionsController> _logger;

    public SessionsController(SessionManager sessionManager, ILogger<SessionsController> logger)
    {
        _sessionManager = sessionManager;
        _logger = logger;
    }

    [HttpGet("{sessionId}")]
    public async Task<IActionResult> GetSession(string sessionId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
            return BadRequest(new { error = "SessionId is required" });

        try
        {
            var session = await _sessionManager.GetOrCreateSessionAsync(sessionId, cancellationToken);
            return Ok(session);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get session {SessionId}", sessionId);
            return StatusCode(500, new { error = "Failed to retrieve session" });
        }
    }

    [HttpGet("{sessionId}/events")]
    public async Task<IActionResult> GetSessionEvents(string sessionId, [FromQuery] int? limit, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
            return BadRequest(new { error = "SessionId is required" });

        try
        {
            var session = await _sessionManager.GetOrCreateSessionAsync(sessionId, cancellationToken);
            var events = limit.HasValue
                ? session.ConversationHistory.TakeLast(limit.Value).ToList()
                : session.ConversationHistory;
            return Ok(events);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get events for session {SessionId}", sessionId);
            return StatusCode(500, new { error = "Failed to retrieve session events" });
        }
    }
}
