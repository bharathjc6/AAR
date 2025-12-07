// =============================================================================
// AAR.Application - Interfaces/IRateLimiter.cs
// Interface for rate limiting external API calls
// =============================================================================

namespace AAR.Application.Interfaces;

/// <summary>
/// Token bucket rate limiter for API calls
/// </summary>
public interface IRateLimiter
{
    /// <summary>
    /// Acquires tokens for an operation
    /// </summary>
    /// <param name="tokens">Number of tokens to acquire</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>True if tokens were acquired, false if rate limited</returns>
    Task<bool> TryAcquireAsync(int tokens, CancellationToken cancellationToken = default);

    /// <summary>
    /// Waits until tokens are available
    /// </summary>
    /// <param name="tokens">Number of tokens to acquire</param>
    /// <param name="timeout">Maximum wait time</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>True if tokens were acquired within timeout</returns>
    Task<bool> WaitForTokensAsync(
        int tokens, 
        TimeSpan timeout,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets current available tokens
    /// </summary>
    int AvailableTokens { get; }

    /// <summary>
    /// Gets estimated wait time for specified tokens
    /// </summary>
    TimeSpan GetEstimatedWaitTime(int tokens);
}

/// <summary>
/// Rate limiter factory
/// </summary>
public interface IRateLimiterFactory
{
    /// <summary>
    /// Gets or creates a rate limiter for a specific endpoint
    /// </summary>
    IRateLimiter GetLimiter(string endpointKey);

    /// <summary>
    /// Gets or creates a rate limiter for an organization
    /// </summary>
    IRateLimiter GetOrganizationLimiter(string organizationId);
}

/// <summary>
/// Resilient API client wrapper with retry, circuit breaker, and rate limiting
/// </summary>
public interface IResilientApiClient
{
    /// <summary>
    /// Executes an API call with resilience policies
    /// </summary>
    Task<T> ExecuteAsync<T>(
        string operationKey,
        Func<CancellationToken, Task<T>> operation,
        int estimatedTokens = 0,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks if the circuit is open for an operation
    /// </summary>
    bool IsCircuitOpen(string operationKey);

    /// <summary>
    /// Gets circuit breaker state
    /// </summary>
    CircuitState GetCircuitState(string operationKey);
}

/// <summary>
/// Circuit breaker states
/// </summary>
public enum CircuitState
{
    Closed,
    Open,
    HalfOpen
}
