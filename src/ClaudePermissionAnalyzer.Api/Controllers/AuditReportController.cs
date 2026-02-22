using ClaudePermissionAnalyzer.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace ClaudePermissionAnalyzer.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AuditReportController : ControllerBase
{
    private readonly AuditReportGenerator _reportGenerator;
    private readonly ILogger<AuditReportController> _logger;

    public AuditReportController(AuditReportGenerator reportGenerator, ILogger<AuditReportController> logger)
    {
        _reportGenerator = reportGenerator;
        _logger = logger;
    }

    [HttpGet("{sessionId}")]
    public async Task<IActionResult> GetReport(string sessionId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            return BadRequest(new { error = "sessionId is required" });
        }

        try
        {
            var report = await _reportGenerator.GenerateReportAsync(sessionId, cancellationToken);
            return Ok(report);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpGet("{sessionId}/html")]
    public async Task<IActionResult> GetHtmlReport(string sessionId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            return BadRequest(new { error = "sessionId is required" });
        }

        try
        {
            var report = await _reportGenerator.GenerateReportAsync(sessionId, cancellationToken);
            var html = _reportGenerator.RenderHtml(report);
            return Content(html, "text/html");
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }
}
