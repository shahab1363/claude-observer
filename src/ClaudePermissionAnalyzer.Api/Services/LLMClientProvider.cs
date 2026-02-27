using ClaudePermissionAnalyzer.Api.Models;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;

namespace ClaudePermissionAnalyzer.Api.Services;

/// <summary>
/// Runtime LLM provider registry and switcher. Maps provider names to factory functions.
/// Reads config.Llm.Provider on each call and delegates to the matching client.
/// Caches the active client and recreates it if the provider changes.
/// For the "claude-persistent" provider, maintains per-session client instances so
/// multiple Claude Code sessions can query the LLM in parallel.
/// Registered as singleton ILLMClient in DI.
/// </summary>
public class LLMClientProvider : ILLMClient, IDisposable
{
    private readonly ConfigurationManager _configManager;
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger<LLMClientProvider> _logger;
    private readonly TerminalOutputService? _terminalOutput;
    private readonly IHttpClientFactory? _httpClientFactory;

    private readonly object _lock = new();
    private ILLMClient? _cachedClient;
    private string? _cachedProvider;

    private readonly Dictionary<string, Func<LlmConfig, ILLMClient>> _factories;

    // Per-session persistent client registry
    private readonly ConcurrentDictionary<string, SessionClientEntry> _sessionClients = new();
    private readonly Timer _cleanupTimer;
    private const int IdleTimeoutMinutes = 10;
    private const int CleanupIntervalMinutes = 5;

    private record SessionClientEntry(ILLMClient Client, DateTime LastUsed);

    public LLMClientProvider(
        ConfigurationManager configManager,
        ILoggerFactory loggerFactory,
        ILogger<LLMClientProvider> logger,
        IHttpClientFactory? httpClientFactory = null,
        TerminalOutputService? terminalOutput = null)
    {
        _configManager = configManager ?? throw new ArgumentNullException(nameof(configManager));
        _loggerFactory = loggerFactory ?? throw new ArgumentNullException(nameof(loggerFactory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _httpClientFactory = httpClientFactory;
        _terminalOutput = terminalOutput;

        _cleanupTimer = new Timer(CleanupIdleSessions, null,
            TimeSpan.FromMinutes(CleanupIntervalMinutes),
            TimeSpan.FromMinutes(CleanupIntervalMinutes));

        _factories = new(StringComparer.OrdinalIgnoreCase)
        {
            ["anthropic-api"] = _ => new AnthropicApiClient(
                CreateHttpClient(),
                _configManager,
                _loggerFactory.CreateLogger<AnthropicApiClient>(),
                _terminalOutput),

            ["claude-cli"] = cfg => new ClaudeCliClient(
                cfg,
                _loggerFactory.CreateLogger<ClaudeCliClient>(),
                _configManager,
                _terminalOutput),

            ["claude-persistent"] = cfg => new PersistentClaudeClient(
                cfg,
                _loggerFactory.CreateLogger<PersistentClaudeClient>(),
                _terminalOutput,
                _configManager),

            ["copilot-cli"] = cfg => new CopilotCliClient(
                cfg,
                _loggerFactory.CreateLogger<CopilotCliClient>(),
                _configManager,
                _terminalOutput),

            ["generic-rest"] = _ => new GenericRestClient(
                CreateHttpClient(),
                _configManager,
                _loggerFactory.CreateLogger<GenericRestClient>(),
                _terminalOutput),
        };
    }

    public async Task<LLMResponse> QueryAsync(string prompt, CancellationToken cancellationToken = default)
    {
        ILLMClient client;
        try
        {
            client = GetClient();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize LLM provider");
            return new LLMResponse
            {
                Success = false,
                SafetyScore = 0,
                Error = $"Failed to initialize LLM provider: {ex.Message}",
                Reasoning = "LLM provider initialization failed"
            };
        }
        return await client.QueryAsync(prompt, cancellationToken);
    }

    /// <summary>
    /// Returns the ILLMClient for the currently configured provider.
    /// Lazily creates clients on first use and caches them.
    /// If the provider changes, the old client is disposed and a new one is created.
    /// </summary>
    public ILLMClient GetClient()
    {
        var config = _configManager.GetConfiguration();
        var provider = config.Llm.Provider ?? "anthropic-api";

        lock (_lock)
        {
            // Return cached client if provider hasn't changed
            if (_cachedClient != null && string.Equals(_cachedProvider, provider, StringComparison.OrdinalIgnoreCase))
                return _cachedClient;

            // Dispose old client if switching providers
            if (_cachedClient is IDisposable disposable)
            {
                _logger.LogInformation("Switching LLM provider from {Old} to {New}", _cachedProvider, provider);
                disposable.Dispose();
                _cachedClient = null;
                _cachedProvider = null;
            }

            if (!_factories.TryGetValue(provider, out var factory))
            {
                _logger.LogWarning("Unknown LLM provider '{Provider}', falling back to anthropic-api", provider);
                factory = _factories["anthropic-api"];
            }

            _cachedClient = factory(config.Llm);
            _cachedProvider = provider;
            _logger.LogInformation("Initialized LLM provider: {Provider}", provider);
            return _cachedClient;
        }
    }

    /// <summary>
    /// Returns an ILLMClient scoped to the given session. For the "claude-persistent" provider,
    /// each session gets its own PersistentClaudeClient so multiple sessions can query in parallel.
    /// For all other providers, returns the shared client (they handle concurrency via HTTP).
    /// </summary>
    public ILLMClient GetClientForSession(string? sessionId)
    {
        var provider = _configManager.GetConfiguration().Llm.Provider ?? "anthropic-api";

        // Only the persistent provider benefits from per-session instances
        if (sessionId == null || !provider.Equals("claude-persistent", StringComparison.OrdinalIgnoreCase))
            return GetClient();

        var entry = _sessionClients.AddOrUpdate(
            sessionId,
            _ =>
            {
                var config = _configManager.GetConfiguration().Llm;
                var client = new PersistentClaudeClient(
                    config,
                    _loggerFactory.CreateLogger<PersistentClaudeClient>(),
                    _terminalOutput,
                    _configManager);
                _logger.LogInformation("Created per-session persistent client for session {SessionId}", sessionId);
                return new SessionClientEntry(client, DateTime.UtcNow);
            },
            (_, existing) => existing with { LastUsed = DateTime.UtcNow });

        return entry.Client;
    }

    private void CleanupIdleSessions(object? state)
    {
        var cutoff = DateTime.UtcNow.AddMinutes(-IdleTimeoutMinutes);
        foreach (var kvp in _sessionClients)
        {
            if (kvp.Value.LastUsed < cutoff)
            {
                if (_sessionClients.TryRemove(kvp.Key, out var removed))
                {
                    _logger.LogInformation("Disposing idle per-session client for session {SessionId}", kvp.Key);
                    if (removed.Client is IDisposable disposable)
                        disposable.Dispose();
                }
            }
        }
    }

    private HttpClient CreateHttpClient()
    {
        if (_httpClientFactory != null)
            return _httpClientFactory.CreateClient("LLMClient");

        // Fallback for tests or when no factory is registered
        return new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
    }

    public void Dispose()
    {
        _cleanupTimer.Dispose();

        // Dispose all per-session clients
        foreach (var kvp in _sessionClients)
        {
            if (_sessionClients.TryRemove(kvp.Key, out var removed) && removed.Client is IDisposable d)
                d.Dispose();
        }

        lock (_lock)
        {
            if (_cachedClient is IDisposable disposable)
                disposable.Dispose();
            _cachedClient = null;
            _cachedProvider = null;
        }
        GC.SuppressFinalize(this);
    }
}
