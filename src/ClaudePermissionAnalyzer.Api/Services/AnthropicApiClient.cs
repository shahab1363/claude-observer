using ClaudePermissionAnalyzer.Api.Models;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.Net;
using System.Text;
using System.Text.Json;

namespace ClaudePermissionAnalyzer.Api.Services;

/// <summary>
/// LLM client that calls the Anthropic Messages API directly via HTTP.
/// No subprocess needed -- fastest and most reliable provider.
/// Uses API key from config or falls back to ~/.claude/config.json primaryApiKey.
/// </summary>
public class AnthropicApiClient : LLMClientBase, ILLMClient
{
    private readonly HttpClient _httpClient;
    private readonly ConfigurationManager _configManager;
    private readonly ILogger<AnthropicApiClient> _logger;

    private const int MaxRetries = 3;
    private const string DefaultBaseUrl = "https://api.anthropic.com";
    private const string ApiVersion = "2023-06-01";

    private static readonly Dictionary<string, string> ModelMapping = new(StringComparer.OrdinalIgnoreCase)
    {
        ["sonnet"] = "claude-sonnet-4-5-20250929",
        ["opus"] = "claude-opus-4-6-20250918",
        ["haiku"] = "claude-haiku-4-5-20251001",
    };

    public AnthropicApiClient(
        HttpClient httpClient,
        ConfigurationManager configManager,
        ILogger<AnthropicApiClient> logger,
        TerminalOutputService? terminalOutput = null)
        : base(configManager, null, terminalOutput)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _configManager = configManager ?? throw new ArgumentNullException(nameof(configManager));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    private string GetApiKey()
    {
        var config = _configManager.GetConfiguration();
        var key = config.Llm.ApiKey;
        if (!string.IsNullOrEmpty(key)) return key;

        return ClaudeCliClient.ReadAnthropicApiKey()
            ?? throw new InvalidOperationException(
                "No API key configured. Set llm.apiKey in config or ensure ~/.claude/config.json has primaryApiKey.");
    }

    private static string MapModel(string model)
    {
        if (ModelMapping.TryGetValue(model, out var mapped))
            return mapped;
        return model; // assume it's already a full model name
    }

    public async Task<LLMResponse> QueryAsync(string prompt, CancellationToken cancellationToken = default)
    {
        var timeout = CurrentTimeout;
        var totalSw = Stopwatch.StartNew();

        for (int attempt = 1; attempt <= MaxRetries; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var sw = Stopwatch.StartNew();

            try
            {
                var config = _configManager.GetConfiguration();
                var baseUrl = config.Llm.ApiBaseUrl?.TrimEnd('/') ?? DefaultBaseUrl;
                var apiKey = GetApiKey();
                var model = MapModel(config.Llm.Model ?? "sonnet");

                var requestBody = BuildRequestBody(model, config.Llm.SystemPrompt, prompt);
                TerminalOutput?.Push("anthropic-api", "info",
                    $"POST {baseUrl}/v1/messages (model: {model}, {prompt.Length} chars): {PreviewPrompt(prompt)}");

                using var request = new HttpRequestMessage(HttpMethod.Post, $"{baseUrl}/v1/messages");
                request.Headers.Add("x-api-key", apiKey);
                request.Headers.Add("anthropic-version", ApiVersion);
                request.Content = new StringContent(requestBody, Encoding.UTF8, "application/json");

                using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                cts.CancelAfter(timeout);

                var response = await _httpClient.SendAsync(request, cts.Token).ConfigureAwait(false);
                sw.Stop();

                // Retry on rate limit or server errors
                if (response.StatusCode == HttpStatusCode.TooManyRequests ||
                    (int)response.StatusCode >= 500)
                {
                    if (attempt < MaxRetries)
                    {
                        var delay = (int)Math.Pow(2, attempt) * 500;
                        TerminalOutput?.Push("anthropic-api", "stderr",
                            $"Attempt {attempt}/{MaxRetries} got {(int)response.StatusCode} -- retrying in {delay}ms...");
                        await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
                        continue;
                    }
                }

                var responseBody = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

                if (!response.IsSuccessStatusCode)
                {
                    var preview = responseBody.Length > 500 ? responseBody[..500] : responseBody;
                    TerminalOutput?.Push("anthropic-api", "stderr",
                        $"API error {(int)response.StatusCode}: {preview}");
                    return CreateFailureResponse(
                        $"Anthropic API returned {(int)response.StatusCode}: {responseBody[..Math.Min(200, responseBody.Length)]}",
                        "API request failed",
                        sw.ElapsedMilliseconds);
                }

                TerminalOutput?.Push("anthropic-api", "stdout",
                    responseBody.Length > 500 ? responseBody[..500] + "..." : responseBody);

                // Extract text from Anthropic response
                var text = ExtractTextFromResponse(responseBody);
                TerminalOutput?.Push("anthropic-api", "info",
                    $"Response received in {sw.ElapsedMilliseconds}ms ({text.Length} chars)");

                // Parse the safety analysis JSON from the LLM text
                var result = ClaudeCliClient.ParseResponse(text);
                result.ElapsedMs = sw.ElapsedMilliseconds;
                _logger.LogInformation("Anthropic API query completed in {Elapsed}ms", sw.ElapsedMilliseconds);
                return result;
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                sw.Stop();
                if (attempt < MaxRetries)
                {
                    TerminalOutput?.Push("anthropic-api", "stderr",
                        $"Attempt {attempt}/{MaxRetries} timed out after {timeout}ms -- retrying...");
                    continue;
                }

                totalSw.Stop();
                return CreateTimeoutResponse("Anthropic API", MaxRetries, timeout, totalSw.ElapsedMilliseconds);
            }
            catch (HttpRequestException ex) when (attempt < MaxRetries)
            {
                sw.Stop();
                TerminalOutput?.Push("anthropic-api", "stderr",
                    $"Attempt {attempt}/{MaxRetries} failed: {ex.Message} -- retrying...");
                await Task.Delay((int)Math.Pow(2, attempt) * 500, cancellationToken).ConfigureAwait(false);
            }
            catch (HttpRequestException ex)
            {
                totalSw.Stop();
                _logger.LogError(ex, "Anthropic API request failed after {MaxRetries} attempts", MaxRetries);
                return CreateFailureResponse(
                    $"Anthropic API request failed: {ex.Message}",
                    "Network error calling Anthropic API",
                    totalSw.ElapsedMilliseconds);
            }
            catch (InvalidOperationException ex)
            {
                _logger.LogError(ex, "Anthropic API configuration error");
                return CreateFailureResponse(ex.Message, ex.Message);
            }
        }

        return CreateRetriesExhaustedResponse("Anthropic API");
    }

    private static string BuildRequestBody(string model, string? systemPrompt, string prompt)
    {
        var sb = new StringBuilder(512);
        sb.Append("{\"model\":");
        sb.Append(JsonSerializer.Serialize(model));
        sb.Append(",\"max_tokens\":1024");

        if (!string.IsNullOrWhiteSpace(systemPrompt))
        {
            sb.Append(",\"system\":");
            sb.Append(JsonSerializer.Serialize(systemPrompt));
        }

        sb.Append(",\"messages\":[{\"role\":\"user\",\"content\":");
        sb.Append(JsonSerializer.Serialize(prompt));
        sb.Append("}]}");

        return sb.ToString();
    }

    private static string ExtractTextFromResponse(string responseBody)
    {
        using var doc = JsonDocument.Parse(responseBody);
        var root = doc.RootElement;

        if (root.TryGetProperty("content", out var content) &&
            content.ValueKind == JsonValueKind.Array)
        {
            foreach (var block in content.EnumerateArray())
            {
                if (block.TryGetProperty("type", out var type) &&
                    type.GetString() == "text" &&
                    block.TryGetProperty("text", out var text))
                {
                    return text.GetString() ?? "";
                }
            }
        }

        return "";
    }
}
