// =============================================================================
// AAR.Api - Middleware/ApiKeyAuthMiddleware.cs
// API key authentication middleware
// =============================================================================

using AAR.Domain.Interfaces;
using AAR.Shared;

namespace AAR.Api.Middleware;

/// <summary>
/// Middleware for API key authentication
/// </summary>
public class ApiKeyAuthMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ApiKeyAuthMiddleware> _logger;

    // Paths that don't require authentication
    private static readonly string[] PublicPaths = 
    [
        "/health",
        "/swagger",
        "/hubs"
    ];

    public ApiKeyAuthMiddleware(RequestDelegate next, ILogger<ApiKeyAuthMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context, IUnitOfWork unitOfWork)
    {
        var path = context.Request.Path.Value ?? "";
        
        // Skip authentication for public paths
        if (IsPublicPath(path))
        {
            await _next(context);
            return;
        }

        // Check for API key header
        if (!context.Request.Headers.TryGetValue("X-API-KEY", out var apiKeyHeader))
        {
            _logger.LogWarning("Missing API key for request to {Path}", path);
            await WriteUnauthorizedResponse(context, DomainErrors.Authentication.MissingApiKey);
            return;
        }

        var apiKeyValue = apiKeyHeader.ToString();
        
        if (string.IsNullOrWhiteSpace(apiKeyValue))
        {
            await WriteUnauthorizedResponse(context, DomainErrors.Authentication.MissingApiKey);
            return;
        }

        // Validate API key
        var apiKey = await unitOfWork.ApiKeys.ValidateKeyAsync(apiKeyValue);
        
        if (apiKey is null)
        {
            _logger.LogWarning("Invalid API key attempt for {Path}", path);
            await WriteUnauthorizedResponse(context, DomainErrors.Authentication.InvalidApiKey);
            return;
        }

        // Save changes (updates LastUsedAt and RequestCount)
        await unitOfWork.SaveChangesAsync();

        // Store API key info in HttpContext for use in controllers
        context.Items["ApiKeyId"] = apiKey.Id;
        context.Items["ApiKeyName"] = apiKey.Name;
        context.Items["ApiKeyScopes"] = apiKey.Scopes?.Split(',') ?? Array.Empty<string>();

        _logger.LogDebug("Authenticated request with API key: {KeyName} ({KeyPrefix}...)", 
            apiKey.Name, apiKey.KeyPrefix);

        await _next(context);
    }

    private static bool IsPublicPath(string path)
    {
        return PublicPaths.Any(p => path.StartsWith(p, StringComparison.OrdinalIgnoreCase));
    }

    private static async Task WriteUnauthorizedResponse(HttpContext context, Error error)
    {
        context.Response.StatusCode = 401;
        context.Response.ContentType = "application/json";
        
        var response = new
        {
            error = new
            {
                error.Code,
                error.Message
            },
            traceId = context.TraceIdentifier
        };
        
        await context.Response.WriteAsJsonAsync(response);
    }
}
