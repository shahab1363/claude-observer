using ClaudePermissionAnalyzer.Api.Models;
using Microsoft.Extensions.Logging;

namespace ClaudePermissionAnalyzer.Api.Handlers;

public class LogOnlyHandler : IHookHandler
{
    private readonly ILogger<LogOnlyHandler> _logger;

    public LogOnlyHandler(ILogger<LogOnlyHandler> logger)
    {
        _logger = logger;
    }

    public Task<HookOutput> HandleAsync(HookInput input, HandlerConfig config, string sessionContext, CancellationToken cancellationToken = default)
    {
        var logLevel = GetLogLevel(config);

        _logger.Log(logLevel, "Hook event {HookEventName} for tool {ToolName} in session {SessionId}",
            input.HookEventName, input.ToolName ?? "N/A", input.SessionId);

        if (input.ToolInput.HasValue)
        {
            _logger.Log(logLevel, "Tool input: {ToolInput}", input.ToolInput.Value.ToString());
        }

        var output = new HookOutput
        {
            AutoApprove = false,
            SafetyScore = 0,
            Reasoning = "Log-only handler - no decision made",
            Category = "logged"
        };

        return Task.FromResult(output);
    }

    private static LogLevel GetLogLevel(HandlerConfig config)
    {
        if (config.Config.TryGetValue("logLevel", out var levelObj) && levelObj is string levelStr)
        {
            return levelStr.ToLowerInvariant() switch
            {
                "trace" => LogLevel.Trace,
                "debug" => LogLevel.Debug,
                "info" or "information" => LogLevel.Information,
                "warn" or "warning" => LogLevel.Warning,
                "error" => LogLevel.Error,
                "critical" => LogLevel.Critical,
                _ => LogLevel.Information
            };
        }

        return LogLevel.Information;
    }
}
