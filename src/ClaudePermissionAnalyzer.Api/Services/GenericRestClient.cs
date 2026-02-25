using ClaudePermissionAnalyzer.Api.Models;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace ClaudePermissionAnalyzer.Api.Services;

/// <summary>
/// LLM client that calls a configurable REST endpoint.
/// Supports any LLM API (OpenAI, local, etc.) via URL/headers/body template.
/// The body template uses {PROMPT} as a placeholder for the user prompt.
/// Response text is extracted using a dot-notation path (e.g. "choices[0].message.content").
/// </summary>
public class GenericRestClient : LLMClientBase, ILLMClient
{
    private readonly HttpClient _httpClient;
    private readonly ConfigurationManager _configManager;
    private readonly ILogger<GenericRestClient> _logger;

    private const int MaxRetries = 3;

    public GenericRestClient(
        HttpClient httpClient,
        ConfigurationManager configManager,
        ILogger<GenericRestClient> logger,
        TerminalOutputService? terminalOutput = null)
        : base(configManager, null, terminalOutput)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _configManager = configManager ?? throw new ArgumentNullException(nameof(configManager));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<LLMResponse> QueryAsync(string prompt, CancellationToken cancellationToken = default)
    {
        var config = _configManager.GetConfiguration();
        var restConfig = config.Llm.GenericRest;

        if (restConfig == null || string.IsNullOrWhiteSpace(restConfig.Url))
        {
            return CreateFailureResponse(
                "Generic REST not configured. Set llm.genericRest.url in config.",
                "Missing REST endpoint configuration");
        }

        var timeout = CurrentTimeout;
        var totalSw = Stopwatch.StartNew();

        for (int attempt = 1; attempt <= MaxRetries; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var sw = Stopwatch.StartNew();

            try
            {
                // Replace {PROMPT} in body template with JSON-escaped prompt
                var escapedPrompt = JsonSerializer.Serialize(prompt);
                // Strip outer quotes -- the template should provide its own quotes: "content": "{PROMPT}"
                var promptValue = escapedPrompt[1..^1];
                var body = restConfig.BodyTemplate?.Replace("{PROMPT}", promptValue) ?? "";

                TerminalOutput?.Push("generic-rest", "info",
                    $"POST {restConfig.Url} ({prompt.Length} chars): {PreviewPrompt(prompt)}");

                using var request = new HttpRequestMessage(HttpMethod.Post, restConfig.Url);

                foreach (var (key, value) in restConfig.Headers)
                {
                    request.Headers.TryAddWithoutValidation(key, value);
                }

                request.Content = new StringContent(body, Encoding.UTF8, "application/json");

                using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                cts.CancelAfter(timeout);

                var response = await _httpClient.SendAsync(request, cts.Token).ConfigureAwait(false);
                sw.Stop();

                // Retry on rate limit or server errors
                if ((int)response.StatusCode == 429 || (int)response.StatusCode >= 500)
                {
                    if (attempt < MaxRetries)
                    {
                        var delay = (int)Math.Pow(2, attempt) * 500;
                        TerminalOutput?.Push("generic-rest", "stderr",
                            $"Attempt {attempt}/{MaxRetries} got {(int)response.StatusCode} -- retrying in {delay}ms...");
                        await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
                        continue;
                    }
                }

                var responseBody = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

                if (!response.IsSuccessStatusCode)
                {
                    var preview = responseBody.Length > 500 ? responseBody[..500] : responseBody;
                    TerminalOutput?.Push("generic-rest", "stderr",
                        $"API error {(int)response.StatusCode}: {preview}");
                    return CreateFailureResponse(
                        $"REST API returned {(int)response.StatusCode}",
                        "API request failed",
                        sw.ElapsedMilliseconds);
                }

                TerminalOutput?.Push("generic-rest", "stdout",
                    responseBody.Length > 500 ? responseBody[..500] + "..." : responseBody);

                // Extract text using configured response path
                var text = ExtractByPath(responseBody, restConfig.ResponsePath);
                TerminalOutput?.Push("generic-rest", "info",
                    $"Response received in {sw.ElapsedMilliseconds}ms ({text.Length} chars)");

                // Parse the safety analysis JSON from the text
                var result = ClaudeCliClient.ParseResponse(text);
                result.ElapsedMs = sw.ElapsedMilliseconds;
                _logger.LogInformation("Generic REST query completed in {Elapsed}ms", sw.ElapsedMilliseconds);
                return result;
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                sw.Stop();
                if (attempt < MaxRetries)
                {
                    TerminalOutput?.Push("generic-rest", "stderr",
                        $"Attempt {attempt}/{MaxRetries} timed out -- retrying...");
                    continue;
                }

                totalSw.Stop();
                return CreateTimeoutResponse("REST API", MaxRetries, timeout, totalSw.ElapsedMilliseconds);
            }
            catch (HttpRequestException ex) when (attempt < MaxRetries)
            {
                sw.Stop();
                TerminalOutput?.Push("generic-rest", "stderr",
                    $"Attempt {attempt}/{MaxRetries} failed: {ex.Message} -- retrying...");
                await Task.Delay((int)Math.Pow(2, attempt) * 500, cancellationToken).ConfigureAwait(false);
            }
            catch (HttpRequestException ex)
            {
                totalSw.Stop();
                _logger.LogError(ex, "Generic REST request failed after {MaxRetries} attempts", MaxRetries);
                return CreateFailureResponse(
                    $"REST API request failed: {ex.Message}",
                    "Network error",
                    totalSw.ElapsedMilliseconds);
            }
        }

        return CreateRetriesExhaustedResponse("REST API");
    }

    /// <summary>
    /// Extracts a string value from JSON using a simple dot-notation path with array index support.
    /// E.g. "choices[0].message.content" navigates: choices -> [0] -> message -> content.
    /// </summary>
    internal static string ExtractByPath(string json, string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return json;

        try
        {
            using var doc = JsonDocument.Parse(json);
            var current = doc.RootElement;

            var segments = path.Split('.');
            foreach (var segment in segments)
            {
                var bracketIdx = segment.IndexOf('[');
                if (bracketIdx >= 0)
                {
                    var prop = segment[..bracketIdx];
                    var closeBracket = segment.IndexOf(']');
                    if (closeBracket < 0) return "";
                    var idxStr = segment[(bracketIdx + 1)..closeBracket];

                    if (!string.IsNullOrEmpty(prop))
                    {
                        if (!current.TryGetProperty(prop, out current))
                            return "";
                    }

                    if (int.TryParse(idxStr, out var arrayIdx) &&
                        current.ValueKind == JsonValueKind.Array &&
                        arrayIdx < current.GetArrayLength())
                    {
                        current = current[arrayIdx];
                    }
                    else
                    {
                        return "";
                    }
                }
                else
                {
                    if (!current.TryGetProperty(segment, out current))
                        return "";
                }
            }

            return current.ValueKind == JsonValueKind.String
                ? current.GetString() ?? ""
                : current.GetRawText();
        }
        catch (JsonException)
        {
            return "";
        }
    }
}
