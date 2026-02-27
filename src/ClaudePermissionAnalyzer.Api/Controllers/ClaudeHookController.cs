using ClaudePermissionAnalyzer.Api.Handlers;
using ClaudePermissionAnalyzer.Api.Models;
using ClaudePermissionAnalyzer.Api.Services;
using ClaudePermissionAnalyzer.Api.Security;
using Microsoft.AspNetCore.Mvc;
using System.Text.Json;

namespace ClaudePermissionAnalyzer.Api.Controllers;

/// <summary>
/// Accepts raw Claude Code hook input and returns Claude-formatted output directly.
/// This endpoint is called by curl from hook commands - no Python needed.
/// </summary>
[ApiController]
[Route("api/hooks")]
public class ClaudeHookController : ControllerBase
{
    private readonly ClaudePermissionAnalyzer.Api.Services.ConfigurationManager _configManager;
    private readonly SessionManager _sessionManager;
    private readonly HookHandlerFactory _handlerFactory;
    private readonly ProfileService _profileService;
    private readonly AdaptiveThresholdService _adaptiveService;
    private readonly EnforcementService _enforcementService;
    private readonly ConsoleStatusService _consoleStatus;
    private readonly ILogger<ClaudeHookController> _logger;

    public ClaudeHookController(
        ClaudePermissionAnalyzer.Api.Services.ConfigurationManager configManager,
        SessionManager sessionManager,
        HookHandlerFactory handlerFactory,
        ProfileService profileService,
        AdaptiveThresholdService adaptiveService,
        EnforcementService enforcementService,
        ConsoleStatusService consoleStatus,
        ILogger<ClaudeHookController> logger)
    {
        _configManager = configManager;
        _sessionManager = sessionManager;
        _handlerFactory = handlerFactory;
        _profileService = profileService;
        _adaptiveService = adaptiveService;
        _enforcementService = enforcementService;
        _consoleStatus = consoleStatus;
        _logger = logger;
    }

    // Tools that should never be approved/denied — they are non-actionable UI interactions.
    // Return {} (no opinion) so Claude handles them normally without hook interference.
    private static readonly HashSet<string> PassthroughTools = new(StringComparer.OrdinalIgnoreCase)
    {
        "AskUserQuestion"
    };

    /// <summary>
    /// Main Claude hook endpoint. Called as:
    ///   curl -s -X POST http://localhost:5050/api/hooks/claude?event=PermissionRequest -H "Content-Type: application/json" -d @-
    /// Returns the exact JSON that Claude Code expects on stdout.
    /// </summary>
    [HttpPost("claude")]
    [Consumes("application/json")]
    [RequestSizeLimit(1_048_576)]
    public async Task<IActionResult> HandleClaudeHook(
        [FromQuery] string @event,
        [FromBody] JsonElement rawInput,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(@event))
            return BadRequest(new { error = "event query parameter is required" });

        // Map raw Claude input to our HookInput
        var input = MapClaudeInput(rawInput, @event);

        if (string.IsNullOrWhiteSpace(input.SessionId))
        {
            _logger.LogWarning("Claude hook missing sessionId");
            // Return empty JSON (no opinion) rather than erroring
            return Content("{}", "application/json");
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
            _logger.LogInformation("Claude hook {Event} for {Tool}", @event, input.ToolName ?? "unknown");

            var appConfig = _configManager.GetConfiguration();
            var isEnforced = _enforcementService.IsEnforced;

            // If not enforced AND analysis disabled in observe mode, just log
            if (!isEnforced && !appConfig.AnalyzeInObserveMode)
            {
                await TryLogEventAsync(input, null, null, cancellationToken);
                return Content("{}", "application/json");
            }

            // Find matching handler
            var handler = _configManager.FindMatchingHandler(input.HookEventName, input.ToolName);

            if (handler == null || handler.Mode == "log-only")
            {
                await TryLogEventAsync(input, null, handler, cancellationToken);
                return Content("{}", "application/json");
            }

            // Non-actionable tools (e.g. AskUserQuestion) should never be approved/denied.
            // Log the event but return no opinion so Claude handles them normally.
            if (!string.IsNullOrEmpty(input.ToolName) && PassthroughTools.Contains(input.ToolName))
            {
                _logger.LogDebug("Passthrough tool {Tool} — skipping analysis", input.ToolName);
                await TryLogEventAsync(input, null, handler, cancellationToken);
                return Content("{}", "application/json");
            }

            // Build session context
            var context = await _sessionManager.BuildContextAsync(input.SessionId, cancellationToken: cancellationToken);

            // Apply profile-based threshold from handler config
            var activeProfile = _profileService.GetActiveProfileKey();
            handler.Threshold = handler.GetThresholdForProfile(activeProfile);
            if (activeProfile == "lockdown")
                handler.AutoApprove = false;

            // Execute handler — always run analysis so logs show scores/decisions
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

            // Log the event (with full analysis results regardless of enforcement)
            await TryLogEventAsync(input, output, handler, cancellationToken);

            // If enforcement is OFF, return empty JSON (Claude asks user as normal)
            // but the analysis was still logged above for visibility
            if (!isEnforced)
            {
                _logger.LogDebug("Observe mode - analyzed {Tool} (score={Score}) but not enforcing",
                    input.ToolName, output.SafetyScore);
                return Content("{}", "application/json");
            }

            // Enforcement ON — return the actual decision to Claude
            var claudeResponse = FormatClaudeResponse(@event, output);
            return Content(JsonSerializer.Serialize(claudeResponse), "application/json");
        }
        catch (OperationCanceledException)
        {
            return Content("{}", "application/json");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing Claude hook for {Event}", @event);
            // On error, return empty JSON so Claude falls through to normal behavior
            return Content("{}", "application/json");
        }
    }

    private static HookInput MapClaudeInput(JsonElement raw, string hookEvent)
    {
        var input = new HookInput
        {
            HookEventName = hookEvent,
            Timestamp = DateTime.UtcNow
        };

        if (raw.TryGetProperty("sessionId", out var sid))
            input.SessionId = sid.GetString() ?? string.Empty;
        else if (raw.TryGetProperty("session_id", out var sid2))
            input.SessionId = sid2.GetString() ?? string.Empty;

        if (raw.TryGetProperty("toolName", out var tn))
            input.ToolName = tn.GetString();
        else if (raw.TryGetProperty("tool_name", out var tn2))
            input.ToolName = tn2.GetString();

        if (raw.TryGetProperty("toolInput", out var ti))
            input.ToolInput = ti;
        else if (raw.TryGetProperty("tool_input", out var ti2))
            input.ToolInput = ti2;

        if (raw.TryGetProperty("cwd", out var cwd))
            input.Cwd = cwd.GetString();

        return input;
    }

    private static object FormatClaudeResponse(string hookEvent, HookOutput output)
    {
        return hookEvent switch
        {
            "PermissionRequest" => FormatPermissionResponse(output),
            "PreToolUse" => FormatPreToolResponse(output),
            "PostToolUse" => FormatPostToolResponse(output),
            _ => new { } // Empty object for log-only events
        };
    }

    private static object FormatPermissionResponse(HookOutput output)
    {
        // Claude Code PermissionRequest format:
        // { hookSpecificOutput: { hookEventName, decision: { behavior, message? } } }
        var behavior = output.AutoApprove ? "allow" : "deny";

        var decision = new Dictionary<string, object>
        {
            ["behavior"] = behavior
        };

        if (!output.AutoApprove)
        {
            var reasoning = output.Reasoning.Length > 1000
                ? output.Reasoning[..1000]
                : output.Reasoning;
            decision["message"] = $"Safety score {output.SafetyScore} below threshold {output.Threshold}. {reasoning}";
        }

        return new
        {
            hookSpecificOutput = new Dictionary<string, object>
            {
                ["hookEventName"] = "PermissionRequest",
                ["decision"] = decision
            }
        };
    }

    private static object FormatPreToolResponse(HookOutput output)
    {
        // Claude Code PreToolUse format:
        // { hookSpecificOutput: { hookEventName, permissionDecision, permissionDecisionReason? } }
        string permissionDecision;
        if (output.SafetyScore >= output.Threshold)
            permissionDecision = "allow";
        else if (output.SafetyScore < 30)
            permissionDecision = "deny";
        else
            permissionDecision = "ask";

        var hookOutput = new Dictionary<string, object>
        {
            ["hookEventName"] = "PreToolUse",
            ["permissionDecision"] = permissionDecision
        };

        if (permissionDecision != "allow")
        {
            var reasoning = output.Reasoning.Length > 1000
                ? output.Reasoning[..1000]
                : output.Reasoning;
            hookOutput["permissionDecisionReason"] = reasoning;
        }

        return new { hookSpecificOutput = hookOutput };
    }

    private static object FormatPostToolResponse(HookOutput output)
    {
        // Claude Code PostToolUse format:
        // { hookSpecificOutput: { hookEventName, additionalContext? }, decision?, reason? }
        if (!string.IsNullOrEmpty(output.SystemMessage))
        {
            return new
            {
                hookSpecificOutput = new
                {
                    hookEventName = "PostToolUse",
                    additionalContext = output.SystemMessage.Length > 500
                        ? output.SystemMessage[..500]
                        : output.SystemMessage
                }
            };
        }
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

            // Build the response JSON that would be (or was) returned to Claude
            string? responseJson = null;
            if (output != null)
            {
                var claudeResponse = FormatClaudeResponse(input.HookEventName, output);
                responseJson = JsonSerializer.Serialize(claudeResponse, new JsonSerializerOptions { WriteIndented = true });
            }

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
                Provider = input.Provider,
                ElapsedMs = output?.ElapsedMs,
                ResponseJson = responseJson
            };

            await _sessionManager.RecordEventAsync(input.SessionId, evt, ct);

            // Update console status line
            _consoleStatus.RecordEvent(decision, input.ToolName, output?.SafetyScore, output?.ElapsedMs);

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
