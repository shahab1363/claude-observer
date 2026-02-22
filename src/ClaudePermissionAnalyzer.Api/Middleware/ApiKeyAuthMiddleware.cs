using System.Security.Cryptography;

namespace ClaudePermissionAnalyzer.Api.Middleware;

/// <summary>
/// Middleware that enforces API key authentication on /api/* endpoints.
/// Static files (dashboard UI) are not protected by this middleware.
/// The API key is read from configuration or the CLAUDE_ANALYZER_API_KEY environment variable.
/// </summary>
public class ApiKeyAuthMiddleware
{
    private readonly RequestDelegate _next;
    private readonly string? _apiKey;
    private readonly ILogger<ApiKeyAuthMiddleware> _logger;
    private const string ApiKeyHeaderName = "X-Api-Key";

    public ApiKeyAuthMiddleware(RequestDelegate next, IConfiguration configuration, ILogger<ApiKeyAuthMiddleware> logger)
    {
        _next = next;
        _logger = logger;

        // Prefer environment variable, then fall back to configuration
        _apiKey = Environment.GetEnvironmentVariable("CLAUDE_ANALYZER_API_KEY")
                  ?? configuration.GetValue<string>("Security:ApiKey");
    }

    public async Task InvokeAsync(HttpContext context)
    {
        // Only protect API endpoints
        if (!context.Request.Path.StartsWithSegments("/api"))
        {
            await _next(context);
            return;
        }

        // If no API key is configured, allow requests (development/local mode)
        if (string.IsNullOrEmpty(_apiKey))
        {
            await _next(context);
            return;
        }

        if (!context.Request.Headers.TryGetValue(ApiKeyHeaderName, out var providedKey))
        {
            _logger.LogWarning("API request rejected: missing {HeaderName} header from {RemoteIp}",
                ApiKeyHeaderName, context.Connection.RemoteIpAddress);
            context.Response.StatusCode = 401;
            await context.Response.WriteAsJsonAsync(new { error = "API key is required. Provide it via the X-Api-Key header." });
            return;
        }

        // Use constant-time comparison to prevent timing attacks
        if (!CryptographicOperations.FixedTimeEquals(
                System.Text.Encoding.UTF8.GetBytes(_apiKey),
                System.Text.Encoding.UTF8.GetBytes(providedKey.ToString())))
        {
            _logger.LogWarning("API request rejected: invalid API key from {RemoteIp}",
                context.Connection.RemoteIpAddress);
            context.Response.StatusCode = 403;
            await context.Response.WriteAsJsonAsync(new { error = "Invalid API key." });
            return;
        }

        await _next(context);
    }
}
