using ClaudePermissionAnalyzer.Api.Handlers;
using ClaudePermissionAnalyzer.Api.Models;
using ClaudePermissionAnalyzer.Api.Services;
using ClaudePermissionAnalyzer.Api.Security;
using Microsoft.AspNetCore.Mvc;
using System.Text.Json;

namespace ClaudePermissionAnalyzer.Api.Controllers;

/// <summary>
/// Accepts GitHub Copilot CLI hook input and returns Copilot-formatted output.
/// Called by bash/powershell scripts that curl to this endpoint.
/// </summary>
[ApiController]
[Route("api/hooks")]
public class CopilotHookController : ControllerBase
{
    private readonly ClaudePermissionAnalyzer.Api.Services.ConfigurationManager _configManager;
    private readonly SessionManager _sessionManager;
    private readonly HookHandlerFactory _handlerFactory;
    private readonly ProfileService _profileService;
    private readonly AdaptiveThresholdService _adaptiveService;
    private readonly EnforcementService _enforcementService;
    private readonly ILogger<CopilotHookController> _logger;

    public CopilotHookController(
        ClaudePermissionAnalyzer.Api.Services.ConfigurationManager configManager,
        SessionManager sessionManager,
        HookHandlerFactory handlerFactory,
        ProfileService profileService,
        AdaptiveThresholdService adaptiveService,
        EnforcementService enforcementService,
        ILogger<CopilotHookController> logger)
    {
        _configManager = configManager;
        _sessionManager = sessionManager;
        _handlerFactory = handlerFactory;
        _profileService = profileService;
        _adaptiveService = adaptiveService;
        _enforcementService = enforcementService;
        _logger = logger;
    }

    /// <summary>
    /// Main Copilot hook endpoint. Called as:
    ///   curl -sS -X POST http://localhost:5050/api/hooks/copilot?event=preToolUse -H "Content-Type: application/json" -d @-
    /// Returns the JSON that Copilot CLI expects.
    /// </summary>
    [HttpPost("copilot")]
    [Consumes("application/json")]
    [RequestSizeLimit(1_048_576)]
    public async Task<IActionResult> HandleCopilotHook(
        [FromQuery] string @event,
        [FromBody] JsonElement rawInput,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(@event))
            return BadRequest(new { error = "event query parameter is required" });

        // Map Copilot event names to normalized names
        var normalizedEvent = NormalizeCopilotEvent(@event);

        // Map raw Copilot input to our HookInput
        var input = MapCopilotInput(rawInput, normalizedEvent);

        if (string.IsNullOrWhiteSpace(input.SessionId))
        {
            // Generate a session ID from cwd if not provided
            input.SessionId = "copilot-" + (input.Cwd?.GetHashCode().ToString("x8") ?? Guid.NewGuid().ToString("N")[..8]);
        }

        // Validate
        if (!InputSanitizer.IsValidSessionId(input.SessionId) ||
            !InputSanitizer.IsValidHookEventName(input.HookEventName) ||
            !InputSanitizer.IsValidToolName(input.ToolName))
        {
            return Content("{}", "application/json");
        }

        try
        {
            _logger.LogInformation("Copilot hook {Event} for {Tool}", normalizedEvent, input.ToolName ?? "unknown");

            var appConfig = _configManager.GetConfiguration();
            var isEnforced = _enforcementService.IsEnforced;

            // Check if Copilot integration is enabled
            if (!appConfig.Copilot.Enabled)
            {
                _logger.LogDebug("Copilot integration is disabled, returning empty response");
                await TryLogEventAsync(input, null, null, cancellationToken);
                return Content("{}", "application/json");
            }

            // If not enforced AND analysis disabled in observe mode, just log
            if (!isEnforced && !appConfig.AnalyzeInObserveMode)
            {
                await TryLogEventAsync(input, null, null, cancellationToken);
                return Content("{}", "application/json");
            }

            // Find matching handler (copilot-specific first, then shared)
            var handler = _configManager.FindMatchingHandler(input.HookEventName, input.ToolName, provider: "copilot");

            if (handler == null || handler.Mode == "log-only")
            {
                await TryLogEventAsync(input, null, handler, cancellationToken);
                return Content("{}", "application/json");
            }

            // Build session context
            var context = await _sessionManager.BuildContextAsync(input.SessionId, cancellationToken: cancellationToken);

            // Apply profile-based threshold
            var activeProfile = _profileService.GetActiveProfileKey();
            handler.Threshold = handler.GetThresholdForProfile(activeProfile);
            if (activeProfile == "lockdown")
                handler.AutoApprove = false;

            // Execute handler
            IHookHandler handlerInstance;
            try
            {
                handlerInstance = _handlerFactory.Create(handler.Mode, handler.PromptTemplate);
            }
            catch (NotSupportedException)
            {
                return Content("{}", "application/json");
            }

            var output = await handlerInstance.HandleAsync(input, handler, context, cancellationToken);

            // Log the event
            await TryLogEventAsync(input, output, handler, cancellationToken);

            // If enforcement is OFF, return empty JSON
            if (!isEnforced)
            {
                _logger.LogDebug("Observe mode - analyzed {Tool} (score={Score}) but not enforcing",
                    input.ToolName, output.SafetyScore);
                return Content("{}", "application/json");
            }

            // Enforcement ON -- return Copilot-formatted decision
            var copilotResponse = FormatCopilotResponse(normalizedEvent, output);
            return Content(JsonSerializer.Serialize(copilotResponse), "application/json");
        }
        catch (OperationCanceledException)
        {
            return Content("{}", "application/json");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing Copilot hook for {Event}", @event);
            return Content("{}", "application/json");
        }
    }

    /// <summary>
    /// Normalizes Copilot event names to our internal format.
    /// Copilot uses camelCase (preToolUse), we use PascalCase (PreToolUse).
    /// </summary>
    private static string NormalizeCopilotEvent(string copilotEvent)
    {
        return copilotEvent switch
        {
            "preToolUse" => "PreToolUse",
            "postToolUse" => "PostToolUse",
            "preToolUseFailure" => "PreToolUse",
            "postToolUseFailure" => "PostToolUseFailure",
            _ => char.ToUpperInvariant(copilotEvent[0]) + copilotEvent[1..] // Best-effort PascalCase
        };
    }

    /// <summary>
    /// Maps Copilot JSON format to our HookInput.
    /// Copilot differences: toolArgs is a JSON string, timestamp is epoch ms.
    /// </summary>
    private static HookInput MapCopilotInput(JsonElement raw, string normalizedEvent)
    {
        var input = new HookInput
        {
            HookEventName = normalizedEvent,
            Provider = "copilot",
            Timestamp = DateTime.UtcNow
        };

        // Session ID (copilot may not always provide one)
        if (raw.TryGetProperty("sessionId", out var sid))
            input.SessionId = sid.GetString() ?? string.Empty;
        else if (raw.TryGetProperty("session_id", out var sid2))
            input.SessionId = sid2.GetString() ?? string.Empty;

        // Tool name
        if (raw.TryGetProperty("toolName", out var tn))
            input.ToolName = tn.GetString();
        else if (raw.TryGetProperty("tool_name", out var tn2))
            input.ToolName = tn2.GetString();

        // Tool args -- Copilot sends toolArgs as a JSON string, parse it to JsonElement
        if (raw.TryGetProperty("toolArgs", out var ta))
        {
            if (ta.ValueKind == JsonValueKind.String)
            {
                var argsStr = ta.GetString();
                if (!string.IsNullOrEmpty(argsStr))
                {
                    try
                    {
                        input.ToolInput = JsonDocument.Parse(argsStr).RootElement;
                    }
                    catch (JsonException)
                    {
                        // If it's not valid JSON, wrap it as a command string
                        input.ToolInput = JsonDocument.Parse($"{{\"command\":\"{argsStr.Replace("\\", "\\\\").Replace("\"", "\\\"")}\"}}").RootElement;
                    }
                }
            }
            else
            {
                input.ToolInput = ta;
            }
        }
        else if (raw.TryGetProperty("toolInput", out var ti))
        {
            input.ToolInput = ti;
        }

        // Working directory
        if (raw.TryGetProperty("cwd", out var cwd))
            input.Cwd = cwd.GetString();

        // Timestamp -- Copilot may send epoch milliseconds
        if (raw.TryGetProperty("timestamp", out var ts))
        {
            if (ts.ValueKind == JsonValueKind.Number && ts.TryGetInt64(out var epochMs))
            {
                input.Timestamp = DateTimeOffset.FromUnixTimeMilliseconds(epochMs).UtcDateTime;
            }
        }

        return input;
    }

    /// <summary>
    /// Formats response in Copilot's expected format.
    /// Copilot expects: { "permissionDecision": "allow|deny|ask", "permissionDecisionReason": "..." }
    /// </summary>
    private static object FormatCopilotResponse(string hookEvent, HookOutput output)
    {
        if (hookEvent == "PreToolUse")
        {
            string decision;
            if (output.SafetyScore >= output.Threshold)
                decision = "allow";
            else if (output.SafetyScore < 30)
                decision = "deny";
            else
                decision = "ask";

            var response = new Dictionary<string, object>
            {
                ["permissionDecision"] = decision
            };

            if (decision != "allow")
            {
                var reasoning = output.Reasoning.Length > 1000
                    ? output.Reasoning[..1000]
                    : output.Reasoning;
                response["permissionDecisionReason"] = reasoning;
            }

            return response;
        }

        // PostToolUse and others -- no decision needed
        return new { };
    }

    private async Task TryLogEventAsync(HookInput input, HookOutput? output, HandlerConfig? handler, CancellationToken ct)
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
                Threshold = output?.Threshold ?? handler?.Threshold,
                Provider = "copilot"
            };

            await _sessionManager.RecordEventAsync(input.SessionId, evt, ct);

            if (output != null && !string.IsNullOrEmpty(input.ToolName))
            {
                try { await _adaptiveService.RecordDecisionAsync(input.ToolName, output.SafetyScore, decision); }
                catch { /* non-fatal */ }
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to log event for session {SessionId}", input.SessionId);
        }
    }
}
