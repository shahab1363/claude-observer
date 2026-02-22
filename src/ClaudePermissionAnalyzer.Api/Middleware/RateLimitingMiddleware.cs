using System.Collections.Concurrent;

namespace ClaudePermissionAnalyzer.Api.Middleware;

/// <summary>
/// Simple sliding-window rate limiting middleware for API endpoints.
/// Uses per-IP tracking with configurable limits.
/// </summary>
public class RateLimitingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<RateLimitingMiddleware> _logger;
    private readonly int _maxRequests;
    private readonly TimeSpan _window;
    private readonly ConcurrentDictionary<string, ClientRequestTracker> _clients = new();
    private readonly Timer _cleanupTimer;

    public RateLimitingMiddleware(
        RequestDelegate next,
        ILogger<RateLimitingMiddleware> logger,
        int maxRequestsPerWindow = 600,
        int windowSeconds = 60)
    {
        _next = next;
        _logger = logger;
        _maxRequests = maxRequestsPerWindow;
        _window = TimeSpan.FromSeconds(windowSeconds);

        // Periodically clean up expired entries to prevent memory growth
        _cleanupTimer = new Timer(CleanupExpiredEntries, null, TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(5));
    }

    public async Task InvokeAsync(HttpContext context)
    {
        // Only rate-limit API endpoints
        if (!context.Request.Path.StartsWithSegments("/api"))
        {
            await _next(context);
            return;
        }

        var clientIp = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        var tracker = _clients.GetOrAdd(clientIp, _ => new ClientRequestTracker());

        if (!tracker.TryAcquire(_maxRequests, _window))
        {
            _logger.LogWarning("Rate limit exceeded for {ClientIp}", clientIp);
            context.Response.StatusCode = 429;
            context.Response.Headers["Retry-After"] = _window.TotalSeconds.ToString("F0");
            await context.Response.WriteAsJsonAsync(new { error = "Rate limit exceeded. Try again later." });
            return;
        }

        await _next(context);
    }

    private void CleanupExpiredEntries(object? state)
    {
        var cutoff = DateTime.UtcNow - _window - _window; // Double the window for safety
        foreach (var kvp in _clients)
        {
            if (kvp.Value.IsExpired(cutoff))
            {
                _clients.TryRemove(kvp.Key, out _);
            }
        }
    }

    private sealed class ClientRequestTracker
    {
        private readonly object _lock = new();
        private readonly Queue<DateTime> _timestamps = new();

        public bool TryAcquire(int maxRequests, TimeSpan window)
        {
            lock (_lock)
            {
                var now = DateTime.UtcNow;
                var cutoff = now - window;

                // Remove expired timestamps
                while (_timestamps.Count > 0 && _timestamps.Peek() < cutoff)
                {
                    _timestamps.Dequeue();
                }

                if (_timestamps.Count >= maxRequests)
                {
                    return false;
                }

                _timestamps.Enqueue(now);
                return true;
            }
        }

        public bool IsExpired(DateTime cutoff)
        {
            lock (_lock)
            {
                return _timestamps.Count == 0 || (_timestamps.Count > 0 && _timestamps.Peek() < cutoff);
            }
        }
    }
}
