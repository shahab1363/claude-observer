using ClaudePermissionAnalyzer.Api.Handlers;
using ClaudePermissionAnalyzer.Api.Models;
using ClaudePermissionAnalyzer.Api.Services;
using ClaudePermissionAnalyzer.Api.Exceptions;
using ClaudePermissionAnalyzer.Api.Security;
using Microsoft.AspNetCore.Mvc;

namespace ClaudePermissionAnalyzer.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AnalyzeController : ControllerBase
{
    private readonly ClaudePermissionAnalyzer.Api.Services.ConfigurationManager _configManager;
    private readonly SessionManager _sessionManager;
    private readonly HookHandlerFactory _handlerFactory;
    private readonly ProfileService _profileService;
    private readonly AdaptiveThresholdService _adaptiveService;
    private readonly EnforcementService _enforcementService;
    private readonly ILogger<AnalyzeController> _logger;

    public AnalyzeController(
        ClaudePermissionAnalyzer.Api.Services.ConfigurationManager configManager,
        SessionManager sessionManager,
        HookHandlerFactory handlerFactory,
        ProfileService profileService,
        AdaptiveThresholdService adaptiveService,
        EnforcementService enforcementService,
        ILogger<AnalyzeController> logger)
    {
        _configManager = configManager;
        _sessionManager = sessionManager;
        _handlerFactory = handlerFactory;
        _profileService = profileService;
        _adaptiveService = adaptiveService;
        _enforcementService = enforcementService;
        _logger = logger;
    }

    [HttpPost]
    [Consumes("application/json")]
    [RequestSizeLimit(1_048_576)] // 1MB hard limit per request
    public async Task<IActionResult> Analyze([FromBody] HookInput input, CancellationToken cancellationToken = default)
    {
        // Validate input
        if (input == null)
        {
            _logger.LogWarning("Received null hook input");
            return BadRequest(new { error = "Request body is required" });
        }

        if (string.IsNullOrWhiteSpace(input.HookEventName))
        {
            _logger.LogWarning("Received hook with empty HookEventName");
            return BadRequest(new { error = "HookEventName is required" });
        }

        if (string.IsNullOrWhiteSpace(input.SessionId))
        {
            _logger.LogWarning("Received hook with empty SessionId");
            return BadRequest(new { error = "SessionId is required" });
        }

        // Validate input fields against injection/overflow
        if (!InputSanitizer.IsValidSessionId(input.SessionId))
        {
            _logger.LogWarning("Received hook with invalid SessionId format: length={Length}", input.SessionId.Length);
            return BadRequest(new { error = "SessionId contains invalid characters or exceeds maximum length" });
        }

        if (!InputSanitizer.IsValidHookEventName(input.HookEventName))
        {
            _logger.LogWarning("Received hook with invalid HookEventName format");
            return BadRequest(new { error = "HookEventName contains invalid characters or exceeds maximum length" });
        }

        if (!InputSanitizer.IsValidToolName(input.ToolName))
        {
            _logger.LogWarning("Received hook with invalid ToolName format");
            return BadRequest(new { error = "ToolName contains invalid characters or exceeds maximum length" });
        }

        if (!InputSanitizer.IsToolInputWithinLimits(input.ToolInput))
        {
            _logger.LogWarning("Received hook with ToolInput exceeding size limit");
            return BadRequest(new { error = "ToolInput exceeds maximum allowed size" });
        }

        try
        {
            _logger.LogInformation("Received {HookType} hook for {ToolName}",
                input.HookEventName, input.ToolName ?? "unknown");

            // Find matching handler — always, regardless of enforcement mode
            var handler = _configManager.FindMatchingHandler(input.HookEventName, input.ToolName);

            if (handler == null || handler.Mode == "log-only")
            {
                await TryLogEventAsync(input, null, handler, cancellationToken);
                return Ok(new { autoApprove = false, message = "No handler configured" });
            }

            // Build session context
            var context = await _sessionManager.BuildContextAsync(input.SessionId, cancellationToken: cancellationToken);

            // Apply profile-based threshold from handler config
            var activeProfile = _profileService.GetActiveProfileKey();
            handler.Threshold = handler.GetThresholdForProfile(activeProfile);
            if (activeProfile == "lockdown")
                handler.AutoApprove = false;

            // Execute handler — always run analysis so logs show scores
            IHookHandler handlerInstance;
            try
            {
                handlerInstance = _handlerFactory.Create(handler.Mode, handler.PromptTemplate);
            }
            catch (NotSupportedException ex)
            {
                _logger.LogError(ex, "Handler creation failed for mode {Mode}", handler.Mode);
                return StatusCode(500, new { error = "Internal processing error" });
            }

            var output = await handlerInstance.HandleAsync(input, handler, context, cancellationToken);

            // Record decision (non-fatal)
            await TryLogEventAsync(input, output, handler, cancellationToken);

            // If enforcement is off, return passthrough (but analysis was logged above)
            if (!_enforcementService.IsEnforced)
            {
                return Ok(new { passthrough = true, logged = true, safetyScore = output.SafetyScore, category = output.Category, reasoning = output.Reasoning });
            }

            return Ok(output);
        }
        catch (StorageException ex)
        {
            _logger.LogError(ex, "Storage error occurred");
            return StatusCode(503, new { error = "Storage subsystem temporarily unavailable" });
        }
        catch (ConfigurationException ex)
        {
            _logger.LogError(ex, "Configuration error occurred");
            return StatusCode(503, new { error = "Service configuration error" });
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("Request cancelled for session {SessionId}", input.SessionId);
            return StatusCode(499, new { error = "Request cancelled" });
        }
        catch (NotSupportedException)
        {
            _logger.LogError("Unsupported handler configuration requested");
            return StatusCode(400, new { error = "Unsupported handler configuration" });
        }
        catch (ArgumentException)
        {
            _logger.LogWarning("Invalid argument in request");
            return BadRequest(new { error = "Invalid request parameters" });
        }
        catch (Exception ex)
        {
            // Catch-all: log full details but return generic error to prevent information leakage
            _logger.LogError(ex, "Unexpected error processing hook for session {SessionId}", input.SessionId);
            return StatusCode(500, new { error = "An unexpected error occurred" });
        }
    }

    private async Task TryLogEventAsync(HookInput input, HookOutput? output, HandlerConfig? handler = null, CancellationToken cancellationToken = default)
    {
        try
        {
            var decision = output switch
            {
                null => "no-handler",
                { AutoApprove: true } => "auto-approved",
                _ => "denied"
            };

            var evt = new SessionEvent
            {
                Type = input.HookEventName,
                ToolName = input.ToolName,
                ToolInput = input.ToolInput,
                Decision = decision,
                SafetyScore = output?.SafetyScore,
                Reasoning = output?.Reasoning,
                Category = output?.Category,
                HandlerName = handler?.Name,
                PromptTemplate = handler?.PromptTemplate != null ? Path.GetFileName(handler.PromptTemplate) : null,
                Threshold = output?.Threshold ?? handler?.Threshold
            };

            await _sessionManager.RecordEventAsync(input.SessionId, evt, cancellationToken);

            // Record decision for adaptive threshold learning
            if (output != null && !string.IsNullOrEmpty(input.ToolName))
            {
                try
                {
                    await _adaptiveService.RecordDecisionAsync(input.ToolName, output.SafetyScore, decision);
                }
                catch (Exception adaptiveEx)
                {
                    _logger.LogDebug(adaptiveEx, "Failed to record adaptive threshold data");
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Request was cancelled, don't log as warning
            _logger.LogDebug("Logging cancelled for session {SessionId}", input.SessionId);
        }
        catch (StorageException ex)
        {
            // Storage failures are more serious - log as error
            _logger.LogError(ex, "Session storage failure for {SessionId} - audit trail may be incomplete", input.SessionId);
        }
        catch (Exception ex)
        {
            // Other failures are non-fatal - log the failure but don't fail the request
            _logger.LogWarning(ex, "Failed to log event for session {SessionId}, continuing with request", input.SessionId);
        }
    }
}
