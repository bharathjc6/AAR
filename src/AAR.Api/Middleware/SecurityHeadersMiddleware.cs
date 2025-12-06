// =============================================================================
// AAR.Api - Middleware/SecurityHeadersMiddleware.cs
// Middleware for adding security headers to all responses
// =============================================================================

namespace AAR.Api.Middleware;

/// <summary>
/// Middleware that adds security headers to all HTTP responses.
/// Implements OWASP security headers recommendations.
/// </summary>
public class SecurityHeadersMiddleware
{
    private readonly RequestDelegate _next;
    private readonly SecurityHeadersOptions _options;

    public SecurityHeadersMiddleware(RequestDelegate next, SecurityHeadersOptions? options = null)
    {
        _next = next;
        _options = options ?? new SecurityHeadersOptions();
    }

    public async Task InvokeAsync(HttpContext context)
    {
        // Add security headers before response is sent
        context.Response.OnStarting(() =>
        {
            var headers = context.Response.Headers;

            // Prevent clickjacking
            if (!headers.ContainsKey("X-Frame-Options"))
            {
                headers["X-Frame-Options"] = _options.XFrameOptions;
            }

            // Prevent MIME type sniffing
            if (!headers.ContainsKey("X-Content-Type-Options"))
            {
                headers["X-Content-Type-Options"] = "nosniff";
            }

            // Control referrer information
            if (!headers.ContainsKey("Referrer-Policy"))
            {
                headers["Referrer-Policy"] = _options.ReferrerPolicy;
            }

            // Disable browser features we don't need
            if (!headers.ContainsKey("Permissions-Policy"))
            {
                headers["Permissions-Policy"] = _options.PermissionsPolicy;
            }

            // Content Security Policy (more restrictive for API)
            if (!headers.ContainsKey("Content-Security-Policy"))
            {
                headers["Content-Security-Policy"] = _options.ContentSecurityPolicy;
            }

            // Prevent XSS in older browsers
            if (!headers.ContainsKey("X-XSS-Protection"))
            {
                headers["X-XSS-Protection"] = "1; mode=block";
            }

            // Remove server header to reduce information leakage
            headers.Remove("Server");
            headers.Remove("X-Powered-By");

            // Add cache control for sensitive endpoints
            if (IsSensitiveEndpoint(context.Request.Path))
            {
                headers["Cache-Control"] = "no-store, no-cache, must-revalidate, proxy-revalidate";
                headers["Pragma"] = "no-cache";
                headers["Expires"] = "0";
            }

            return Task.CompletedTask;
        });

        await _next(context);
    }

    private static bool IsSensitiveEndpoint(PathString path)
    {
        var pathValue = path.Value ?? "";
        return pathValue.Contains("/api/", StringComparison.OrdinalIgnoreCase) ||
               pathValue.Contains("/auth/", StringComparison.OrdinalIgnoreCase) ||
               pathValue.Contains("/admin/", StringComparison.OrdinalIgnoreCase);
    }
}

/// <summary>
/// Configuration options for security headers
/// </summary>
public class SecurityHeadersOptions
{
    /// <summary>
    /// X-Frame-Options header value. Default: DENY
    /// </summary>
    public string XFrameOptions { get; set; } = "DENY";

    /// <summary>
    /// Referrer-Policy header value. Default: strict-origin-when-cross-origin
    /// </summary>
    public string ReferrerPolicy { get; set; } = "strict-origin-when-cross-origin";

    /// <summary>
    /// Permissions-Policy header value. Disables unnecessary browser features.
    /// </summary>
    public string PermissionsPolicy { get; set; } = 
        "accelerometer=(), camera=(), geolocation=(), gyroscope=(), magnetometer=(), microphone=(), payment=(), usb=()";

    /// <summary>
    /// Content-Security-Policy header value.
    /// Default is restrictive for API endpoints.
    /// </summary>
    public string ContentSecurityPolicy { get; set; } = 
        "default-src 'none'; frame-ancestors 'none'; form-action 'none'";

    /// <summary>
    /// Strict-Transport-Security header value for HTTPS.
    /// Only applied in production.
    /// </summary>
    public string StrictTransportSecurity { get; set; } = 
        "max-age=31536000; includeSubDomains; preload";
}

/// <summary>
/// Extension methods for security headers middleware
/// </summary>
public static class SecurityHeadersMiddlewareExtensions
{
    /// <summary>
    /// Adds security headers middleware to the pipeline
    /// </summary>
    public static IApplicationBuilder UseSecurityHeaders(
        this IApplicationBuilder app,
        SecurityHeadersOptions? options = null)
    {
        return app.UseMiddleware<SecurityHeadersMiddleware>(options ?? new SecurityHeadersOptions());
    }

    /// <summary>
    /// Adds security headers middleware with configuration action
    /// </summary>
    public static IApplicationBuilder UseSecurityHeaders(
        this IApplicationBuilder app,
        Action<SecurityHeadersOptions> configure)
    {
        var options = new SecurityHeadersOptions();
        configure(options);
        return app.UseMiddleware<SecurityHeadersMiddleware>(options);
    }
}
