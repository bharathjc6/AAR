// =============================================================================
// AAR.Api - Middleware/RateLimitingMiddleware.cs
// Custom rate limiting implementation using .NET Rate Limiter
// =============================================================================

using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;

namespace AAR.Api.Middleware;

/// <summary>
/// Configuration options for rate limiting
/// </summary>
public class RateLimitingOptions
{
    /// <summary>
    /// Global rate limit (requests per minute per IP)
    /// </summary>
    public int GlobalRequestsPerMinute { get; set; } = 100;

    /// <summary>
    /// Rate limit for authenticated API key requests (per minute)
    /// </summary>
    public int ApiKeyRequestsPerMinute { get; set; } = 300;

    /// <summary>
    /// Rate limit for upload endpoints (per hour)
    /// </summary>
    public int UploadRequestsPerHour { get; set; } = 10;

    /// <summary>
    /// Burst limit (concurrent requests)
    /// </summary>
    public int BurstLimit { get; set; } = 20;

    /// <summary>
    /// Queue limit for waiting requests
    /// </summary>
    public int QueueLimit { get; set; } = 10;

    /// <summary>
    /// Enable sliding window vs fixed window
    /// </summary>
    public bool UseSlidingWindow { get; set; } = true;
}

/// <summary>
/// Extension methods for configuring rate limiting
/// </summary>
public static class RateLimitingExtensions
{
    /// <summary>
    /// Adds rate limiting services with security-focused configuration
    /// </summary>
    public static IServiceCollection AddSecureRateLimiting(
        this IServiceCollection services,
        Action<RateLimitingOptions>? configure = null)
    {
        var options = new RateLimitingOptions();
        configure?.Invoke(options);

        services.AddRateLimiter(limiter =>
        {
            // Global rate limiter by IP address
            limiter.AddPolicy("global", context =>
            {
                var clientIp = GetClientIpAddress(context);

                return RateLimitPartition.GetSlidingWindowLimiter(
                    partitionKey: clientIp,
                    factory: _ => new SlidingWindowRateLimiterOptions
                    {
                        PermitLimit = options.GlobalRequestsPerMinute,
                        Window = TimeSpan.FromMinutes(1),
                        SegmentsPerWindow = 6, // 10-second segments
                        QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                        QueueLimit = options.QueueLimit
                    });
            });

            // API key rate limiter (higher limits for authenticated requests)
            limiter.AddPolicy("apikey", context =>
            {
                // Try to get API key from context
                var apiKeyId = context.Items.TryGetValue("ApiKeyId", out var id) && id is Guid guidId
                    ? guidId.ToString()
                    : GetClientIpAddress(context);

                return RateLimitPartition.GetSlidingWindowLimiter(
                    partitionKey: apiKeyId,
                    factory: _ => new SlidingWindowRateLimiterOptions
                    {
                        PermitLimit = options.ApiKeyRequestsPerMinute,
                        Window = TimeSpan.FromMinutes(1),
                        SegmentsPerWindow = 6,
                        QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                        QueueLimit = options.QueueLimit
                    });
            });

            // Strict rate limiter for upload endpoints
            limiter.AddPolicy("upload", context =>
            {
                var apiKeyId = context.Items.TryGetValue("ApiKeyId", out var id) && id is Guid guidId
                    ? guidId.ToString()
                    : GetClientIpAddress(context);

                return RateLimitPartition.GetFixedWindowLimiter(
                    partitionKey: apiKeyId,
                    factory: _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = options.UploadRequestsPerHour,
                        Window = TimeSpan.FromHours(1),
                        QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                        QueueLimit = 2
                    });
            });

            // Concurrency limiter for expensive operations
            limiter.AddPolicy("expensive", context =>
            {
                var clientIp = GetClientIpAddress(context);

                return RateLimitPartition.GetConcurrencyLimiter(
                    partitionKey: clientIp,
                    factory: _ => new ConcurrencyLimiterOptions
                    {
                        PermitLimit = 5,
                        QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                        QueueLimit = 2
                    });
            });

            // Configure rejection response
            limiter.OnRejected = async (context, token) =>
            {
                context.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
                context.HttpContext.Response.ContentType = "application/json";

                // Add retry-after header if available
                if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
                {
                    context.HttpContext.Response.Headers.RetryAfter = 
                        ((int)retryAfter.TotalSeconds).ToString();
                }

                var response = new
                {
                    error = new
                    {
                        code = "RateLimitExceeded",
                        message = "Too many requests. Please retry after the specified time."
                    },
                    traceId = context.HttpContext.TraceIdentifier,
                    retryAfterSeconds = context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retry) 
                        ? (int)retry.TotalSeconds 
                        : 60
                };

                await context.HttpContext.Response.WriteAsJsonAsync(response, token);
            };
        });

        return services;
    }

    private static string GetClientIpAddress(HttpContext context)
    {
        // Check for forwarded headers (behind proxy/load balancer)
        var forwardedFor = context.Request.Headers["X-Forwarded-For"].FirstOrDefault();
        if (!string.IsNullOrEmpty(forwardedFor))
        {
            // Take the first IP in the chain (original client)
            var ip = forwardedFor.Split(',', StringSplitOptions.RemoveEmptyEntries)
                .FirstOrDefault()?.Trim();
            if (!string.IsNullOrEmpty(ip))
            {
                return ip;
            }
        }

        // Check for real IP header (Cloudflare, nginx)
        var realIp = context.Request.Headers["X-Real-IP"].FirstOrDefault();
        if (!string.IsNullOrEmpty(realIp))
        {
            return realIp;
        }

        // Fallback to connection remote IP
        return context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
    }
}
