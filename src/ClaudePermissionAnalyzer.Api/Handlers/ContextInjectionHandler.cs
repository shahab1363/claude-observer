using ClaudePermissionAnalyzer.Api.Models;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.Text;

namespace ClaudePermissionAnalyzer.Api.Handlers;

public class ContextInjectionHandler : IHookHandler
{
    private readonly ILogger<ContextInjectionHandler> _logger;

    public ContextInjectionHandler(ILogger<ContextInjectionHandler> logger)
    {
        _logger = logger;
    }

    public async Task<HookOutput> HandleAsync(HookInput input, HandlerConfig config, string sessionContext, CancellationToken cancellationToken = default)
    {
        var contextParts = new List<string>();

        var injectGitBranch = GetConfigBool(config, "injectGitBranch", false);
        var injectRecentErrors = GetConfigBool(config, "injectRecentErrors", false);

        if (injectGitBranch && !string.IsNullOrEmpty(input.Cwd))
        {
            var branch = await GetGitBranchAsync(input.Cwd, cancellationToken);
            if (!string.IsNullOrEmpty(branch))
            {
                contextParts.Add($"[Git Branch: {branch}]");
            }
        }

        if (injectRecentErrors && !string.IsNullOrEmpty(sessionContext))
        {
            var errors = ExtractRecentErrors(sessionContext);
            if (!string.IsNullOrEmpty(errors))
            {
                contextParts.Add($"[Recent Errors: {errors}]");
            }
        }

        var additionalContext = contextParts.Count > 0
            ? string.Join(" ", contextParts)
            : null;

        _logger.LogInformation("Context injection for session {SessionId}: {Context}",
            input.SessionId, additionalContext ?? "no context injected");

        return new HookOutput
        {
            AutoApprove = false,
            SafetyScore = 0,
            Reasoning = "Context injection handler",
            Category = "context-injection",
            SystemMessage = additionalContext
        };
    }

    private async Task<string?> GetGitBranchAsync(string workingDir, CancellationToken cancellationToken)
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
            startInfo.ArgumentList.Add("rev-parse");
            startInfo.ArgumentList.Add("--abbrev-ref");
            startInfo.ArgumentList.Add("HEAD");

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
            _logger.LogDebug(ex, "Failed to get git branch for {WorkingDir}", workingDir);
            return null;
        }
    }

    private static string? ExtractRecentErrors(string sessionContext)
    {
        var lines = sessionContext.Split('\n');
        var errors = lines
            .Where(l => l.Contains("error", StringComparison.OrdinalIgnoreCase) ||
                        l.Contains("failed", StringComparison.OrdinalIgnoreCase))
            .TakeLast(3)
            .ToList();

        return errors.Count > 0 ? string.Join("; ", errors.Select(e => e.Trim())) : null;
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
