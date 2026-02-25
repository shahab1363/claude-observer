using ClaudePermissionAnalyzer.Api.Services;
using Microsoft.AspNetCore.Mvc;
using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ClaudePermissionAnalyzer.Api.Controllers;

[ApiController]
[Route("api/debug")]
public class DebugController : ControllerBase
{
    private readonly LLMClientProvider _llmClientProvider;
    private readonly PromptTemplateService _promptTemplateService;
    private readonly SessionManager _sessionManager;
    private readonly TerminalOutputService _terminalOutput;
    private readonly ILogger<DebugController> _logger;

    public DebugController(
        LLMClientProvider llmClientProvider,
        PromptTemplateService promptTemplateService,
        SessionManager sessionManager,
        TerminalOutputService terminalOutput,
        ILogger<DebugController> logger)
    {
        _llmClientProvider = llmClientProvider;
        _promptTemplateService = promptTemplateService;
        _sessionManager = sessionManager;
        _terminalOutput = terminalOutput;
        _logger = logger;
    }

    [HttpPost("llm")]
    public async Task<IActionResult> DebugLlm([FromBody] DebugLlmRequest request, CancellationToken cancellationToken)
    {
        string prompt;

        if (!string.IsNullOrEmpty(request.RawPrompt))
        {
            // Raw mode: use prompt directly
            prompt = request.RawPrompt;
        }
        else
        {
            // Replay mode: reconstruct prompt from log entry data
            string? templateContent = null;
            if (!string.IsNullOrEmpty(request.PromptTemplate))
            {
                templateContent = _promptTemplateService.GetTemplate(request.PromptTemplate);
            }

            string? sessionContext = null;
            if (!string.IsNullOrEmpty(request.SessionId))
            {
                try
                {
                    sessionContext = await _sessionManager.BuildContextAsync(request.SessionId, cancellationToken: cancellationToken);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to build session context for debug request");
                }
            }

            prompt = PromptBuilder.Build(templateContent, request.ToolName, request.Cwd, request.ToolInput, sessionContext);
        }

        _terminalOutput.Push("debug", "info", "--- Debug LLM Request ---");

        var sw = Stopwatch.StartNew();
        LLMResponse llmResponse;
        try
        {
            var client = _llmClientProvider.GetClient();
            llmResponse = await client.QueryAsync(prompt, cancellationToken);
        }
        catch (Exception ex)
        {
            sw.Stop();
            _terminalOutput.Push("debug", "stderr", $"Debug LLM error: {ex.Message}");
            return Ok(new DebugLlmResponse
            {
                Success = false,
                Error = ex.Message,
                ElapsedMs = sw.ElapsedMilliseconds,
                PromptUsed = prompt
            });
        }
        sw.Stop();

        _terminalOutput.Push("debug", "info",
            $"Debug result: score={llmResponse.SafetyScore} category={llmResponse.Category} elapsed={sw.ElapsedMilliseconds}ms");

        return Ok(new DebugLlmResponse
        {
            Success = llmResponse.Success,
            SafetyScore = llmResponse.SafetyScore,
            Reasoning = llmResponse.Reasoning,
            Category = llmResponse.Category,
            Error = llmResponse.Error,
            ElapsedMs = sw.ElapsedMilliseconds,
            PromptUsed = prompt
        });
    }
}

public class DebugLlmRequest
{
    // Replay mode fields
    [JsonPropertyName("toolName")]
    public string? ToolName { get; set; }

    [JsonPropertyName("toolInput")]
    public JsonElement? ToolInput { get; set; }

    [JsonPropertyName("promptTemplate")]
    public string? PromptTemplate { get; set; }

    [JsonPropertyName("sessionId")]
    public string? SessionId { get; set; }

    [JsonPropertyName("hookEventName")]
    public string? HookEventName { get; set; }

    [JsonPropertyName("cwd")]
    public string? Cwd { get; set; }

    // Raw mode field
    [JsonPropertyName("rawPrompt")]
    public string? RawPrompt { get; set; }
}

public class DebugLlmResponse
{
    [JsonPropertyName("success")]
    public bool Success { get; set; }

    [JsonPropertyName("safetyScore")]
    public int SafetyScore { get; set; }

    [JsonPropertyName("reasoning")]
    public string? Reasoning { get; set; }

    [JsonPropertyName("category")]
    public string? Category { get; set; }

    [JsonPropertyName("error")]
    public string? Error { get; set; }

    [JsonPropertyName("elapsedMs")]
    public long ElapsedMs { get; set; }

    [JsonPropertyName("promptUsed")]
    public string? PromptUsed { get; set; }
}
