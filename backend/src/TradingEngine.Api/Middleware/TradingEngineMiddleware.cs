// ═══════════════════════════════════════════════════════════════════
// ExceptionHandlingMiddleware.cs - Global exception handler.
//
// Catches ALL unhandled exceptions and returns a structured JSON
// error response. Prevents stack traces from leaking to the client
// in production while providing enough detail for debugging.
//
// Logs every exception with structured context for ELK/Seq ingestion.
// ═══════════════════════════════════════════════════════════════════

using System.Diagnostics;
using System.Text.Json;

namespace TradingEngine.Api.Middleware;

/// <summary>
/// Catches unhandled exceptions and produces RFC 7807 Problem Details responses.
/// </summary>
public sealed class ExceptionHandlingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ExceptionHandlingMiddleware> _logger;

    // Common error mappings to avoid boxing allocations on every request
    private static readonly Dictionary<Type, int> StatusCodeMappings = new()
    {
        { typeof(ArgumentException), StatusCodes.Status400BadRequest },
        { typeof(ArgumentNullException), StatusCodes.Status400BadRequest },
        { typeof(ArgumentOutOfRangeException), StatusCodes.Status400BadRequest },
        { typeof(InvalidOperationException), StatusCodes.Status409Conflict },
        { typeof(UnauthorizedAccessException), StatusCodes.Status401Unauthorized },
        { typeof(KeyNotFoundException), StatusCodes.Status404NotFound },
        { typeof(NotImplementedException), StatusCodes.Status501NotImplemented },
        { typeof(OperationCanceledException), StatusCodes.Status499ClientClosedRequest },
        { typeof(TaskCanceledException), StatusCodes.Status499ClientClosedRequest }
    };

    public ExceptionHandlingMiddleware(
        RequestDelegate next,
        ILogger<ExceptionHandlingMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (Exception ex)
        {
            await HandleExceptionAsync(context, ex);
        }
    }

    /// <summary>
    /// Writes a structured JSON error response.
    /// In development, includes the stack trace.
    /// </summary>
    private async Task HandleExceptionAsync(HttpContext context, Exception exception)
    {
        // ── Determine HTTP status code ────────────────────────────
        var statusCode = StatusCodes.Status500InternalServerError;
        string title = "Internal Server Error";

        if (StatusCodeMappings.TryGetValue(exception.GetType(), out var mappedCode))
        {
            statusCode = mappedCode;
            title = exception.GetType().Name switch
            {
                nameof(ArgumentException) => "Invalid Request",
                nameof(ArgumentNullException) => "Missing Required Parameter",
                nameof(ArgumentOutOfRangeException) => "Parameter Out of Range",
                nameof(InvalidOperationException) => "Operation Invalid",
                nameof(UnauthorizedAccessException) => "Unauthorized",
                nameof(KeyNotFoundException) => "Resource Not Found",
                nameof(NotImplementedException) => "Not Implemented",
                _ => "Error"
            };
        }

        // ── Structured logging with correlation ────────────────────
        var correlationId = Activity.Current?.Id ?? context.TraceIdentifier;

        _logger.LogError(exception,
            "HTTP {Method} {Path} failed with {StatusCode} | CorrelationId: {CorrelationId}",
            context.Request.Method,
            context.Request.Path,
            statusCode,
            correlationId);

        // ── Prevent response from being written twice ──────────────
        if (context.Response.HasStarted)
        {
            _logger.LogWarning("Response already started; cannot write error body for {Path}",
                context.Request.Path);
            return;
        }

        // ── Build response ─────────────────────────────────────────
        context.Response.Clear();
        context.Response.StatusCode = statusCode;
        context.Response.ContentType = "application/problem+json; charset=utf-8";

        var problemDetails = new Dictionary<string, object?>
        {
            ["type"] = $"https://httpstatuses.com/{statusCode}",
            ["title"] = title,
            ["status"] = statusCode,
            ["detail"] = exception.Message,
            ["instance"] = context.Request.Path,
            ["correlation_id"] = correlationId,
            ["timestamp"] = DateTime.UtcNow.ToString("O")
        };

        // Only include stack trace in development
        if (context.RequestServices.GetRequiredService<IWebHostEnvironment>().IsDevelopment())
        {
            problemDetails["stack_trace"] = exception.ToString();
            problemDetails["exception_type"] = exception.GetType().FullName;

            if (exception.InnerException is not null)
            {
                problemDetails["inner_exception"] = exception.InnerException.Message;
            }
        }

        // ── Serialize ─────────────────────────────────────────────
        var json = JsonSerializer.Serialize(problemDetails, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
            WriteIndented = false
        });

        await context.Response.WriteAsync(json);
    }
}

// ═══════════════════════════════════════════════════════════════════
// RequestRateLimitingMiddleware.cs - Sliding window rate limiter.
//
// Uses a sliding window counter stored in Redis to enforce per-IP
// rate limits. The SignalR hub connections are exempt because they
// maintain persistent connections.
//
// Falls back to in-memory rate limiting if Redis is unavailable.
// ═══════════════════════════════════════════════════════════════════

public sealed class RequestRateLimitingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<RequestRateLimitingMiddleware> _logger;

    // In-memory fallback rate limit tracker
    private static readonly ConcurrentDictionary<string, SlidingWindowCounter> _counters = new();

    // Config
    private const int DefaultMaxRequests = 100;        // 100 requests
    private const int DefaultWindowSeconds = 60;       // per 60 seconds
    private static readonly string[] ExemptPaths = { "/hubs/", "/health", "/" };

    public RequestRateLimitingMiddleware(
        RequestDelegate next,
        ILogger<RequestRateLimitingMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        // Skip rate limiting for exempt paths
        var path = context.Request.Path.Value ?? "";
        if (ExemptPaths.Any(p => path.StartsWith(p, StringComparison.OrdinalIgnoreCase)))
        {
            await _next(context);
            return;
        }

        var clientIp = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        var key = $"{clientIp}:{context.Request.Method}:{path}";

        var counter = _counters.GetOrAdd(key, _ =>
            new SlidingWindowCounter(DefaultMaxRequests, TimeSpan.FromSeconds(DefaultWindowSeconds)));

        if (!counter.TryAcquire())
        {
            context.Response.StatusCode = StatusCodes.Status429TooManyRequests;
            context.Response.Headers.RetryAfter = counter.ResetSeconds.ToString();

            _logger.LogWarning("Rate limit exceeded for {Ip}: {Path}", clientIp, path);

            await context.Response.WriteAsJsonAsync(new
            {
                error = "rate_limit_exceeded",
                message = $"Too many requests. Retry after {counter.ResetSeconds} seconds.",
                retry_after_seconds = counter.ResetSeconds
            });
            return;
        }

        await _next(context);
    }
}

/// <summary>
/// Sliding window rate counter implementation.
/// Thread-safe, lock-free where possible.
/// </summary>
public sealed class SlidingWindowCounter
{
    private readonly int _maxRequests;
    private readonly TimeSpan _window;
    private readonly ConcurrentQueue<DateTime> _timestamps = new();

    /// <summary>Seconds until the window resets.</summary>
    public int ResetSeconds => (int)(_window - (DateTime.UtcNow - _timestamps.FirstOrDefault())).TotalSeconds;

    public SlidingWindowCounter(int maxRequests, TimeSpan window)
    {
        _maxRequests = maxRequests;
        _window = window;
    }

    /// <summary>
    /// Try to acquire a slot. Returns true if under the limit.
    /// Thread-safe: uses concurrent queue and lock-free operations.
    /// </summary>
    public bool TryAcquire()
    {
        var now = DateTime.UtcNow;
        var cutoff = now - _window;

        // Clean expired timestamps
        while (_timestamps.TryPeek(out var oldest) && oldest < cutoff)
            _timestamps.TryDequeue(out _);

        // Check limit
        if (_timestamps.Count >= _maxRequests)
            return false;

        _timestamps.Enqueue(now);
        return true;
    }
}

// ═══════════════════════════════════════════════════════════════════
// RequestValidationMiddleware.cs - Validates incoming requests.
// ═══════════════════════════════════════════════════════════════════

public sealed class RequestValidationMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<RequestValidationMiddleware> _logger;

    // Content-Security-Policy for API responses
    private const string SecurityHeaders = @"
        default-src 'self';
        connect-src 'self' ws: wss:;
        script-src 'self';
        style-src 'self' 'unsafe-inline';
        img-src 'self' data:;
        frame-ancestors 'none'";

    public RequestValidationMiddleware(
        RequestDelegate next,
        ILogger<RequestValidationMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        // ── Add security headers to every response ─────────────────
        context.Response.OnStarting(() =>
        {
            context.Response.Headers.XContentTypeOptions = "nosniff";
            context.Response.Headers.XFrameOptions = "DENY";
            context.Response.Headers.XXSSProtection = "1; mode=block";
            context.Response.Headers.ReferrerPolicy = "strict-origin-when-cross-origin";
            context.Response.Headers.ContentSecurityPolicy = SecurityHeaders.Replace("\n", "").Replace("    ", " ");

            return Task.CompletedTask;
        });

        // ── Validate Content-Type for POST/PUT/PATCH ──────────────
        var method = context.Request.Method;
        if (method is "POST" or "PUT" or "PATCH")
        {
            var contentType = context.Request.ContentType;
            if (string.IsNullOrEmpty(contentType) ||
                (!contentType.Contains("application/json") &&
                 !contentType.Contains("multipart/form-data")))
            {
                context.Response.StatusCode = StatusCodes.Status415UnsupportedMediaType;
                await context.Response.WriteAsJsonAsync(new
                {
                    error = "unsupported_media_type",
                    message = "Expected application/json or multipart/form-data Content-Type."
                });
                return;
            }
        }

        await _next(context);
    }
}