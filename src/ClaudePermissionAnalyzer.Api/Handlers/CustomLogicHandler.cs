using ClaudePermissionAnalyzer.Api.Models;
using ClaudePermissionAnalyzer.Api.Services;
using Microsoft.Extensions.Logging;
using System.Diagnostics;

namespace ClaudePermissionAnalyzer.Api.Handlers;

public class CustomLogicHandler : IHookHandler
{
    private readonly SessionManager _sessionManager;
    private readonly ILogger<CustomLogicHandler> _logger;

    public CustomLogicHandler(SessionManager sessionManager, ILogger<CustomLogicHandler> logger)
    {
        _sessionManager = sessionManager;
        _logger = logger;
    }

    public async Task<HookOutput> HandleAsync(HookInput input, HandlerConfig config, string sessionContext, CancellationToken cancellationToken = default)
    {
        return input.HookEventName switch
        {
            "SessionStart" => await HandleSessionStartAsync(input, config, cancellationToken),
            "SessionEnd" => await HandleSessionEndAsync(input, config, cancellationToken),
            _ => HandleDefault(input)
        };
    }

    private async Task<HookOutput> HandleSessionStartAsync(HookInput input, HandlerConfig config, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Session started: {SessionId}", input.SessionId);

        var contextParts = new List<string>();

        if (GetConfigBool(config, "loadProjectContext", false) && !string.IsNullOrEmpty(input.Cwd))
        {
            var projectContext = await LoadProjectContextAsync(input.Cwd, cancellationToken);
            if (!string.IsNullOrEmpty(projectContext))
            {
                contextParts.Add(projectContext);
            }
        }

        if (GetConfigBool(config, "checkGitStatus", false) && !string.IsNullOrEmpty(input.Cwd))
        {
            var gitStatus = await GetGitStatusAsync(input.Cwd, cancellationToken);
            if (!string.IsNullOrEmpty(gitStatus))
            {
                contextParts.Add($"Git status: {gitStatus}");
            }
        }

        // Ensure session is created
        await _sessionManager.GetOrCreateSessionAsync(input.SessionId, cancellationToken);

        return new HookOutput
        {
            AutoApprove = false,
            SafetyScore = 0,
            Reasoning = "Session initialized",
            Category = "session-start",
            SystemMessage = contextParts.Count > 0 ? string.Join("\n", contextParts) : null
        };
    }

    private async Task<HookOutput> HandleSessionEndAsync(HookInput input, HandlerConfig config, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Session ended: {SessionId}", input.SessionId);

        if (GetConfigBool(config, "archiveSession", false))
        {
            _logger.LogInformation("Archiving session {SessionId}", input.SessionId);
            // Session data is already persisted by SessionManager; logging the end event is the archive action
            var evt = new SessionEvent
            {
                Type = "session-end",
                Content = "Session ended"
            };
            await _sessionManager.RecordEventAsync(input.SessionId, evt, cancellationToken);
        }

        return new HookOutput
        {
            AutoApprove = false,
            SafetyScore = 0,
            Reasoning = "Session cleanup complete",
            Category = "session-end"
        };
    }

    private HookOutput HandleDefault(HookInput input)
    {
        _logger.LogInformation("Custom logic handler invoked for {HookEventName} in session {SessionId}",
            input.HookEventName, input.SessionId);

        return new HookOutput
        {
            AutoApprove = false,
            SafetyScore = 0,
            Reasoning = "Custom logic handler - no specific logic for this event type",
            Category = "custom"
        };
    }

    private async Task<string?> LoadProjectContextAsync(string workingDir, CancellationToken cancellationToken)
    {
        try
        {
            // Look for common project descriptor files
            var projectFiles = new[] { "package.json", "*.csproj", "*.sln", "Cargo.toml", "pyproject.toml", "go.mod" };
            foreach (var pattern in projectFiles)
            {
                var files = Directory.GetFiles(workingDir, pattern, SearchOption.TopDirectoryOnly);
                if (files.Length > 0)
                {
                    var fileName = Path.GetFileName(files[0]);
                    return $"Project type detected: {fileName}";
                }
            }
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to load project context for {WorkingDir}", workingDir);
            return null;
        }
    }

    private async Task<string?> GetGitStatusAsync(string workingDir, CancellationToken cancellationToken)
    {
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "git",
                WorkingDirectory = workingDir,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            startInfo.ArgumentList.Add("status");
            startInfo.ArgumentList.Add("--short");

            using var process = new Process { StartInfo = startInfo };

            if (!process.Start())
                return null;

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(5000);

            var output = await process.StandardOutput.ReadToEndAsync(cts.Token);
            await process.WaitForExitAsync(cts.Token);

            return process.ExitCode == 0 ? output.Trim() : null;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to get git status for {WorkingDir}", workingDir);
            return null;
        }
    }

    private static bool GetConfigBool(HandlerConfig config, string key, bool defaultValue)
    {
        if (config.Config.TryGetValue(key, out var value))
        {
            return value switch
            {
                bool b => b,
                string s => bool.TryParse(s, out var result) && result,
                System.Text.Json.JsonElement je => je.ValueKind == System.Text.Json.JsonValueKind.True,
                _ => defaultValue
            };
        }
        return defaultValue;
    }
}
