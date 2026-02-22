using Microsoft.AspNetCore.Mvc;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ClaudePermissionAnalyzer.Api.Controllers;

[ApiController]
[Route("api/claude-settings")]
public class ClaudeSettingsController : ControllerBase
{
    private readonly ILogger<ClaudeSettingsController> _logger;
    private static readonly string SettingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        ".claude",
        "settings.json");

    public ClaudeSettingsController(ILogger<ClaudeSettingsController> logger)
    {
        _logger = logger;
    }

    [HttpGet]
    public IActionResult GetSettings()
    {
        try
        {
            if (!System.IO.File.Exists(SettingsPath))
                return Ok(new { path = SettingsPath, exists = false, content = "{}" });

            var json = System.IO.File.ReadAllText(SettingsPath);
            // Validate it's valid JSON
            JsonNode.Parse(json);
            return Ok(new { path = SettingsPath, exists = true, content = json });
        }
        catch (JsonException)
        {
            var raw = System.IO.File.ReadAllText(SettingsPath);
            return Ok(new { path = SettingsPath, exists = true, content = raw, parseError = true });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to read Claude settings");
            return StatusCode(500, new { error = "Failed to read settings: " + ex.Message });
        }
    }

    [HttpPut]
    [Consumes("application/json")]
    public IActionResult SaveSettings([FromBody] JsonElement body)
    {
        try
        {
            if (!body.TryGetProperty("content", out var contentElement))
                return BadRequest(new { error = "content field is required" });

            var content = contentElement.GetString();
            if (content == null)
                return BadRequest(new { error = "content must be a string" });

            // Validate JSON before saving
            var parsed = JsonNode.Parse(content);
            if (parsed == null)
                return BadRequest(new { error = "content is not valid JSON" });

            // Pretty-print
            var pretty = parsed.ToJsonString(new JsonSerializerOptions { WriteIndented = true });

            var dir = Path.GetDirectoryName(SettingsPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            System.IO.File.WriteAllText(SettingsPath, pretty);
            _logger.LogInformation("Claude settings.json updated via web UI");

            return Ok(new { saved = true, path = SettingsPath });
        }
        catch (JsonException ex)
        {
            return BadRequest(new { error = "Invalid JSON: " + ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save Claude settings");
            return StatusCode(500, new { error = "Failed to save: " + ex.Message });
        }
    }
}
