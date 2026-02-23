using ClaudePermissionAnalyzer.Api.Models;
using Microsoft.Extensions.Logging;

namespace ClaudePermissionAnalyzer.Api.Services;

/// <summary>
/// Runtime LLM provider switcher. Reads config.Llm.Provider on each call
/// and delegates to the matching client (claude-cli or copilot-cli).
/// Registered as singleton ILLMClient in DI.
/// </summary>
public class LLMClientProvider : ILLMClient, IDisposable
{
    private readonly ConfigurationManager _configManager;
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger<LLMClientProvider> _logger;

    private readonly object _lock = new();
    private ClaudeCliClient? _claudeClient;
    private PersistentClaudeClient? _persistentClaudeClient;
    private CopilotCliClient? _copilotClient;

    public LLMClientProvider(
        ConfigurationManager configManager,
        ILoggerFactory loggerFactory,
        ILogger<LLMClientProvider> logger)
    {
        _configManager = configManager ?? throw new ArgumentNullException(nameof(configManager));
        _loggerFactory = loggerFactory ?? throw new ArgumentNullException(nameof(loggerFactory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public Task<LLMResponse> QueryAsync(string prompt, CancellationToken cancellationToken = default)
    {
        var client = GetClient();
        return client.QueryAsync(prompt, cancellationToken);
    }

    /// <summary>
    /// Returns the ILLMClient for the currently configured provider.
    /// Lazily creates clients on first use.
    /// </summary>
    public ILLMClient GetClient()
    {
        var config = _configManager.GetConfiguration();
        var provider = config.Llm.Provider ?? "claude-cli";

        return provider.ToLowerInvariant() switch
        {
            "copilot-cli" => GetCopilotClient(config.Llm),
            _ => GetClaudeClient(config.Llm), // claude-cli is default
        };
    }

    private ILLMClient GetClaudeClient(LlmConfig llmConfig)
    {
        if (llmConfig.PersistentProcess)
        {
            lock (_lock)
            {
                _persistentClaudeClient ??= new PersistentClaudeClient(
                    llmConfig,
                    _loggerFactory.CreateLogger<PersistentClaudeClient>());
                return _persistentClaudeClient;
            }
        }

        lock (_lock)
        {
            _claudeClient ??= new ClaudeCliClient(
                llmConfig,
                _loggerFactory.CreateLogger<ClaudeCliClient>(),
                _configManager);
            return _claudeClient;
        }
    }

    private CopilotCliClient GetCopilotClient(LlmConfig llmConfig)
    {
        lock (_lock)
        {
            _copilotClient ??= new CopilotCliClient(
                llmConfig,
                _loggerFactory.CreateLogger<CopilotCliClient>());
            return _copilotClient;
        }
    }

    public void Dispose()
    {
        lock (_lock)
        {
            (_persistentClaudeClient as IDisposable)?.Dispose();
            _persistentClaudeClient = null;
            _claudeClient = null;
            _copilotClient = null;
        }
        GC.SuppressFinalize(this);
    }
}
