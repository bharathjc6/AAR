// =============================================================================
// AAR.Application - Interfaces/IPreflightService.cs
// Interface for preflight validation service
// =============================================================================

using AAR.Application.DTOs;

namespace AAR.Application.Interfaces;

/// <summary>
/// Service for preflight validation of repositories
/// </summary>
public interface IPreflightService
{
    /// <summary>
    /// Analyzes a repository before processing
    /// </summary>
    /// <param name="request">Preflight request</param>
    /// <param name="organizationId">Organization ID for quota checking</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Preflight analysis result</returns>
    Task<PreflightResponse> AnalyzeAsync(
        PreflightRequest request, 
        string organizationId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Analyzes a zip file stream for preflight
    /// </summary>
    Task<PreflightResponse> AnalyzeZipAsync(
        Stream zipStream,
        string organizationId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Estimates cost for given parameters
    /// </summary>
    decimal EstimateCost(long totalBytes, int fileCount);

    /// <summary>
    /// Estimates token count for given size
    /// </summary>
    long EstimateTokenCount(long totalBytes);
}
