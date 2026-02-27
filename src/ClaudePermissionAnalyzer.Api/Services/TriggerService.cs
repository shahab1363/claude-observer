using ClaudePermissionAnalyzer.Api.Models;
using System.Text;
using System.Text.Json;

namespace ClaudePermissionAnalyzer.Api.Services;

/// <summary>
/// Fires HTTP webhook triggers on matching hook events.
/// Triggers run fire-and-forget so they never block the hook response.
/// </summary>
public class TriggerService
{
    private readonly ConfigurationManager _configManager;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<TriggerService> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = false };

    public TriggerService(
        ConfigurationManager configManager,
        IHttpClientFactory httpClientFactory,
        ILogger<TriggerService> logger)
    {
        _configManager = configManager;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    /// <summary>
    /// Match event against configured trigger rules and fire webhooks.
    /// This method returns immediately; HTTP calls happen in the background.
    /// </summary>
    public void FireAsync(string decision, string? category, SessionEvent evt)
    {
        try
        {
            var config = _configManager.GetConfiguration();
            var triggers = config.Triggers;

            if (!triggers.Enabled || triggers.Rules.Count == 0)
                return;

            var matchingRules = triggers.Rules.Where(r => RuleMatches(r, decision, category)).ToList();

            if (matchingRules.Count == 0)
                return;

            var payload = new
            {
                @event = evt.Type,
                toolName = evt.ToolName,
                decision,
                safetyScore = evt.SafetyScore,
                category = evt.Category,
                reasoning = evt.Reasoning,
                timestamp = evt.Timestamp,
                sessionId = (string?)null // session ID not on SessionEvent
            };

            var json = JsonSerializer.Serialize(payload, JsonOptions);

            foreach (var rule in matchingRules)
            {
                _ = Task.Run(() => SendWebhookAsync(rule, json));
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to evaluate trigger rules");
        }
    }

    private bool RuleMatches(TriggerRule rule, string decision, string? category)
    {
        if (rule.Event == "*")
            return true;

        if (string.Equals(rule.Event, decision, StringComparison.OrdinalIgnoreCase))
            return true;

        // "dangerous" matches when category is "dangerous" regardless of decision
        if (string.Equals(rule.Event, "dangerous", StringComparison.OrdinalIgnoreCase)
            && string.Equals(category, "dangerous", StringComparison.OrdinalIgnoreCase))
            return true;

        return false;
    }

    private async Task SendWebhookAsync(TriggerRule rule, string json)
    {
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            var client = _httpClientFactory.CreateClient("LLMClient");

            var request = new HttpRequestMessage(
                rule.Method.Equals("GET", StringComparison.OrdinalIgnoreCase) ? HttpMethod.Get : HttpMethod.Post,
                rule.Url)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };

            var response = await client.SendAsync(request, cts.Token);

            _logger.LogDebug("Trigger '{Name}' fired to {Url}: {Status}",
                rule.Name, rule.Url, response.StatusCode);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Trigger '{Name}' failed to fire to {Url}", rule.Name, rule.Url);
        }
    }
}
