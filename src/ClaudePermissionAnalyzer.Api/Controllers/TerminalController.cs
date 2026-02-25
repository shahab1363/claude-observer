using ClaudePermissionAnalyzer.Api.Services;
using Microsoft.AspNetCore.Mvc;
using System.Text.Json;

namespace ClaudePermissionAnalyzer.Api.Controllers;

[ApiController]
[Route("api/terminal")]
public class TerminalController : ControllerBase
{
    private readonly TerminalOutputService _terminalOutput;
    private readonly ILogger<TerminalController> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public TerminalController(
        TerminalOutputService terminalOutput,
        ILogger<TerminalController> logger)
    {
        _terminalOutput = terminalOutput;
        _logger = logger;
    }

    [HttpGet("buffer")]
    public IActionResult GetBuffer()
    {
        var lines = _terminalOutput.GetBuffer();
        return Ok(lines);
    }

    [HttpPost("clear")]
    public IActionResult Clear()
    {
        _terminalOutput.Clear();
        return Ok(new { cleared = true });
    }

    [HttpGet("stream")]
    public async Task Stream(CancellationToken cancellationToken)
    {
        Response.Headers["Content-Type"] = "text/event-stream";
        Response.Headers["Cache-Control"] = "no-cache";
        Response.Headers["Connection"] = "keep-alive";

        void OnLineReceived(object? sender, TerminalLine line)
        {
            try
            {
                var json = JsonSerializer.Serialize(line, JsonOptions);
                var sseData = $"data: {json}\n\n";
                var bytes = System.Text.Encoding.UTF8.GetBytes(sseData);
                Response.Body.WriteAsync(bytes, 0, bytes.Length);
                Response.Body.FlushAsync();
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Failed to write terminal SSE data");
            }
        }

        _terminalOutput.LineReceived += OnLineReceived;

        try
        {
            // Send initial keepalive
            await Response.WriteAsync(": connected\n\n", cancellationToken);
            await Response.Body.FlushAsync(cancellationToken);

            // Keep connection open until client disconnects
            await Task.Delay(Timeout.Infinite, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            // Client disconnected
        }
        finally
        {
            _terminalOutput.LineReceived -= OnLineReceived;
        }
    }
}
