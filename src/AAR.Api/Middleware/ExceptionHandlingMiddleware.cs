// =============================================================================
// AAR.Api - Middleware/ExceptionHandlingMiddleware.cs
// Global exception handling middleware
// =============================================================================

using System.Net;
using System.Text.Json;
using AAR.Shared;

namespace AAR.Api.Middleware;

/// <summary>
/// Global exception handling middleware that converts exceptions to proper HTTP responses
/// </summary>
public class ExceptionHandlingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ExceptionHandlingMiddleware> _logger;

    public ExceptionHandlingMiddleware(RequestDelegate next, ILogger<ExceptionHandlingMiddleware> logger)
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

    private async Task HandleExceptionAsync(HttpContext context, Exception exception)
    {
        var (statusCode, error) = MapExceptionToError(exception);
        
        _logger.LogError(exception, "Unhandled exception: {ErrorCode} - {ErrorMessage}", 
            error.Code, error.Message);

        context.Response.ContentType = "application/json";
        context.Response.StatusCode = (int)statusCode;

        var response = new ErrorResponse
        {
            Error = error,
            TraceId = context.TraceIdentifier
        };

        var json = JsonSerializer.Serialize(response, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });

        await context.Response.WriteAsync(json);
    }

    private static (HttpStatusCode, Error) MapExceptionToError(Exception exception)
    {
        return exception switch
        {
            ArgumentException argEx => (
                HttpStatusCode.BadRequest, 
                DomainErrors.Validation.InvalidRequest(argEx.Message)),
            
            UnauthorizedAccessException => (
                HttpStatusCode.Unauthorized, 
                DomainErrors.Authentication.InvalidApiKey),
            
            FileNotFoundException => (
                HttpStatusCode.NotFound, 
                new Error("File.NotFound", "The requested file was not found")),
            
            InvalidOperationException opEx => (
                HttpStatusCode.BadRequest, 
                new Error("Operation.Invalid", opEx.Message)),
            
            TimeoutException => (
                HttpStatusCode.GatewayTimeout, 
                new Error("Request.Timeout", "The operation timed out")),
            
            OperationCanceledException => (
                HttpStatusCode.BadRequest, 
                new Error("Request.Cancelled", "The operation was cancelled")),
            
            _ => (
                HttpStatusCode.InternalServerError, 
                new Error("Server.Error", "An unexpected error occurred. Please try again later."))
        };
    }
}

/// <summary>
/// Standard error response format
/// </summary>
public record ErrorResponse
{
    public required Error Error { get; init; }
    public required string TraceId { get; init; }
}
