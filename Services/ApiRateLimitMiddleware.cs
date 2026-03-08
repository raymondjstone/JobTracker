using System.Collections.Concurrent;

namespace JobTracker.Services;

/// <summary>
/// Simple sliding-window rate limiter for API endpoints.
/// Limits requests per IP address to prevent abuse.
/// </summary>
public class ApiRateLimitMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ApiRateLimitMiddleware> _logger;
    private readonly int _maxRequests;
    private readonly TimeSpan _window;
    private readonly ConcurrentDictionary<string, RequestTracker> _clients = new();

    public ApiRateLimitMiddleware(RequestDelegate next, ILogger<ApiRateLimitMiddleware> logger,
        int maxRequests = 600, int windowSeconds = 60)
    {
        _next = next;
        _logger = logger;
        _maxRequests = maxRequests;
        _window = TimeSpan.FromSeconds(windowSeconds);
    }

    public async Task InvokeAsync(HttpContext context)
    {
        // Only rate-limit API endpoints
        if (!context.Request.Path.StartsWithSegments("/api"))
        {
            await _next(context);
            return;
        }

        var clientId = GetClientIdentifier(context);
        var tracker = _clients.GetOrAdd(clientId, _ => new RequestTracker());

        if (!tracker.TryAdd(_window, _maxRequests))
        {
            _logger.LogWarning("Rate limit exceeded for {ClientId} on {Path}", clientId, context.Request.Path);
            context.Response.StatusCode = StatusCodes.Status429TooManyRequests;
            context.Response.Headers["Retry-After"] = _window.TotalSeconds.ToString("F0");
            await context.Response.WriteAsJsonAsync(new { error = "Rate limit exceeded. Try again later." });
            return;
        }

        await _next(context);

        // Periodic cleanup of stale entries
        if (Random.Shared.Next(100) == 0)
        {
            CleanupStaleEntries();
        }
    }

    private static string GetClientIdentifier(HttpContext context)
    {
        // Use X-Forwarded-For if behind a proxy, otherwise use remote IP
        var forwarded = context.Request.Headers["X-Forwarded-For"].FirstOrDefault();
        if (!string.IsNullOrEmpty(forwarded))
        {
            return forwarded.Split(',')[0].Trim();
        }

        return context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
    }

    private void CleanupStaleEntries()
    {
        var cutoff = DateTime.UtcNow - _window - _window; // 2x window for cleanup
        foreach (var kvp in _clients)
        {
            if (kvp.Value.IsStale(cutoff))
            {
                _clients.TryRemove(kvp.Key, out _);
            }
        }
    }

    private class RequestTracker
    {
        private readonly Queue<DateTime> _timestamps = new();
        private readonly object _lock = new();

        public bool TryAdd(TimeSpan window, int maxRequests)
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

        public bool IsStale(DateTime cutoff)
        {
            lock (_lock)
            {
                return _timestamps.Count == 0 || (_timestamps.Count > 0 && _timestamps.Peek() < cutoff);
            }
        }
    }
}

public static class ApiRateLimitExtensions
{
    /// <summary>
    /// Adds API rate limiting middleware.
    /// </summary>
    /// <param name="app">The application builder.</param>
    /// <param name="maxRequests">Maximum requests per window (default: 60).</param>
    /// <param name="windowSeconds">Window size in seconds (default: 60).</param>
    public static IApplicationBuilder UseApiRateLimit(this IApplicationBuilder app,
        int maxRequests = 60, int windowSeconds = 60)
    {
        return app.UseMiddleware<ApiRateLimitMiddleware>(maxRequests, windowSeconds);
    }
}
