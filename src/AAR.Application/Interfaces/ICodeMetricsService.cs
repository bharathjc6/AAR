// =============================================================================
// AAR.Application - Interfaces/ICodeMetricsService.cs
// Abstraction for code metrics computation
// =============================================================================

using AAR.Domain.ValueObjects;

namespace AAR.Application.Interfaces;

/// <summary>
/// Interface for computing code metrics
/// </summary>
public interface ICodeMetricsService
{
    /// <summary>
    /// Computes metrics for a source file
    /// </summary>
    /// <param name="content">File content</param>
    /// <param name="filePath">File path (for determining language)</param>
    /// <returns>Computed metrics</returns>
    FileMetrics ComputeMetrics(string content, string filePath);

    /// <summary>
    /// Computes metrics for a source file asynchronously
    /// </summary>
    /// <param name="content">File content</param>
    /// <param name="filePath">File path (for determining language)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Computed metrics</returns>
    Task<FileMetrics> CalculateMetricsAsync(string content, string filePath, CancellationToken cancellationToken = default);

    /// <summary>
    /// Computes metrics for a source file asynchronously by reading from the file path
    /// </summary>
    /// <param name="filePath">Full path to the source file</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Computed metrics</returns>
    Task<FileMetrics> CalculateMetricsAsync(string filePath, CancellationToken cancellationToken = default);

    /// <summary>
    /// Computes aggregate metrics for multiple files
    /// </summary>
    /// <param name="fileMetrics">Collection of file metrics</param>
    /// <returns>Aggregated metrics</returns>
    AggregateMetrics ComputeAggregateMetrics(IEnumerable<FileMetrics> fileMetrics);
}

/// <summary>
/// Aggregate metrics for a project
/// </summary>
public record AggregateMetrics
{
    public int TotalFiles { get; init; }
    public int TotalLinesOfCode { get; init; }
    public int TotalLines { get; init; }
    public int TotalTypes { get; init; }
    public int TotalMethods { get; init; }
    public double AverageCyclomaticComplexity { get; init; }
    public int MaxCyclomaticComplexity { get; init; }
}
