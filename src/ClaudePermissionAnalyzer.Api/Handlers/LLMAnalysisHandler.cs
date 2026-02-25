using ClaudePermissionAnalyzer.Api.Models;
using ClaudePermissionAnalyzer.Api.Services;

namespace ClaudePermissionAnalyzer.Api.Handlers;

public class LLMAnalysisHandler : IHookHandler
{
    private readonly ILLMClient _llmClient;
    private readonly string? _promptTemplate;

    public LLMAnalysisHandler(ILLMClient llmClient, string? promptTemplate)
    {
        _llmClient = llmClient;
        _promptTemplate = promptTemplate;
    }

    public async Task<HookOutput> HandleAsync(HookInput input, HandlerConfig config, string sessionContext, CancellationToken cancellationToken = default)
    {
        var prompt = PromptBuilder.Build(_promptTemplate, input.ToolName, input.Cwd, input.ToolInput, sessionContext);
        var llmResponse = await _llmClient.QueryAsync(prompt, cancellationToken);

        if (!llmResponse.Success)
        {
            return new HookOutput
            {
                AutoApprove = false,
                SafetyScore = 0,
                Reasoning = llmResponse.Error ?? "LLM query failed",
                Category = "error",
                Threshold = config.Threshold,
                ElapsedMs = llmResponse.ElapsedMs
            };
        }

        // Clamp safety score to valid range to prevent LLM manipulation
        var clampedScore = Math.Clamp(llmResponse.SafetyScore, 0, 100);

        var autoApprove = config.AutoApprove && clampedScore >= config.Threshold;

        return new HookOutput
        {
            AutoApprove = autoApprove,
            SafetyScore = clampedScore,
            Reasoning = llmResponse.Reasoning,
            Category = llmResponse.Category,
            Threshold = config.Threshold,
            Interrupt = llmResponse.Category == "dangerous",
            ElapsedMs = llmResponse.ElapsedMs
        };
    }
}
