using ClaudePermissionAnalyzer.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace ClaudePermissionAnalyzer.Api.Controllers;

[ApiController]
[Route("api/prompts")]
public class PromptsController : ControllerBase
{
    private readonly PromptTemplateService _promptTemplateService;
    private readonly ILogger<PromptsController> _logger;

    public PromptsController(PromptTemplateService promptTemplateService, ILogger<PromptsController> logger)
    {
        _promptTemplateService = promptTemplateService;
        _logger = logger;
    }

    [HttpGet]
    public IActionResult GetAllTemplates()
    {
        var templates = _promptTemplateService.GetAllTemplates();
        return Ok(templates);
    }

    [HttpGet("names")]
    public IActionResult GetTemplateNames()
    {
        var names = _promptTemplateService.GetTemplateNames();
        return Ok(names);
    }

    [HttpGet("{templateName}")]
    public IActionResult GetTemplate(string templateName)
    {
        if (string.IsNullOrWhiteSpace(templateName))
            return BadRequest(new { error = "Template name is required" });

        var template = _promptTemplateService.GetTemplate(templateName);
        if (template == null)
            return NotFound(new { error = $"Template '{templateName}' not found" });

        return Ok(new { name = templateName, content = template });
    }

    [HttpPut("{templateName}")]
    public IActionResult SaveTemplate(string templateName, [FromBody] TemplateSaveRequest request)
    {
        if (string.IsNullOrWhiteSpace(templateName))
            return BadRequest(new { error = "Template name is required" });

        if (request == null || string.IsNullOrEmpty(request.Content))
            return BadRequest(new { error = "Template content is required" });

        var success = _promptTemplateService.SaveTemplate(templateName, request.Content);
        if (!success)
            return StatusCode(500, new { error = "Failed to save template" });

        return Ok(new { message = "Template saved successfully" });
    }
}

public class TemplateSaveRequest
{
    public string Content { get; set; } = string.Empty;
}
