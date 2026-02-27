using ClaudePermissionAnalyzer.Api.Handlers;
using ClaudePermissionAnalyzer.Api.Services;
using Microsoft.Extensions.Logging;

namespace ClaudePermissionAnalyzer.Api.Services;

public class HookHandlerFactory
{
    private readonly IServiceProvider _serviceProvider;
    private readonly LLMClientProvider _llmClientProvider;
    private readonly PromptTemplateService _promptTemplateService;
    private readonly SessionManager _sessionManager;
    private readonly ILogger<HookHandlerFactory> _logger;

    public HookHandlerFactory(
        IServiceProvider serviceProvider,
        LLMClientProvider llmClientProvider,
        PromptTemplateService promptTemplateService,
        SessionManager sessionManager,
        ILogger<HookHandlerFactory> logger)
    {
        _serviceProvider = serviceProvider;
        _llmClientProvider = llmClientProvider;
        _promptTemplateService = promptTemplateService;
        _sessionManager = sessionManager;
        _logger = logger;
    }

    public virtual IHookHandler Create(string mode, string? promptTemplateName = null, string? sessionId = null)
    {
        string? promptTemplate = null;
        if (!string.IsNullOrEmpty(promptTemplateName))
        {
            promptTemplate = _promptTemplateService.GetTemplate(promptTemplateName);
        }

        var llmClient = _llmClientProvider.GetClientForSession(sessionId);

        return mode switch
        {
            "llm-analysis" => new LLMAnalysisHandler(llmClient, promptTemplate),
            "llm-validation" => new LLMAnalysisHandler(llmClient, promptTemplate),
            "log-only" => new LogOnlyHandler(
                _serviceProvider.GetRequiredService<ILogger<LogOnlyHandler>>()),
            "context-injection" => new ContextInjectionHandler(
                _serviceProvider.GetRequiredService<ILogger<ContextInjectionHandler>>()),
            "custom-logic" => new CustomLogicHandler(
                _sessionManager,
                _serviceProvider.GetRequiredService<ILogger<CustomLogicHandler>>()),
            _ => throw new NotSupportedException($"Handler mode '{mode}' is not supported")
        };
    }
}
