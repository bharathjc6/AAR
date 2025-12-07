// =============================================================================
// AAR.Application - Messaging/Contracts.cs
// MassTransit message contracts for analysis jobs
// =============================================================================

namespace AAR.Application.Messaging;

/// <summary>
/// Command to start analysis of a project
/// </summary>
public record StartAnalysisCommand
{
    /// <summary>
    /// Project ID to analyze
    /// </summary>
    public Guid ProjectId { get; init; }
    
    /// <summary>
    /// When the command was created
    /// </summary>
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
    
    /// <summary>
    /// Correlation ID for tracking
    /// </summary>
    public Guid CorrelationId { get; init; } = Guid.NewGuid();
    
    /// <summary>
    /// Priority level (higher = more urgent)
    /// </summary>
    public int Priority { get; init; } = 0;
    
    /// <summary>
    /// Optional metadata for the analysis job
    /// </summary>
    public Dictionary<string, string>? Metadata { get; init; }
}

/// <summary>
/// Event published when analysis is started
/// </summary>
public record AnalysisStartedEvent
{
    /// <summary>
    /// Project ID being analyzed
    /// </summary>
    public Guid ProjectId { get; init; }
    
    /// <summary>
    /// When analysis started
    /// </summary>
    public DateTime StartedAt { get; init; } = DateTime.UtcNow;
    
    /// <summary>
    /// Correlation ID for tracking
    /// </summary>
    public Guid CorrelationId { get; init; }
}

/// <summary>
/// Event published when analysis is completed
/// </summary>
public record AnalysisCompletedEvent
{
    /// <summary>
    /// Project ID that was analyzed
    /// </summary>
    public Guid ProjectId { get; init; }
    
    /// <summary>
    /// Report ID if analysis was successful
    /// </summary>
    public Guid? ReportId { get; init; }
    
    /// <summary>
    /// Whether analysis was successful
    /// </summary>
    public bool Success { get; init; }
    
    /// <summary>
    /// Error message if analysis failed
    /// </summary>
    public string? ErrorMessage { get; init; }
    
    /// <summary>
    /// When analysis completed
    /// </summary>
    public DateTime CompletedAt { get; init; } = DateTime.UtcNow;
    
    /// <summary>
    /// Duration of analysis
    /// </summary>
    public TimeSpan Duration { get; init; }
    
    /// <summary>
    /// Correlation ID for tracking
    /// </summary>
    public Guid CorrelationId { get; init; }
}

/// <summary>
/// Event published when analysis fails
/// </summary>
public record AnalysisFailedEvent
{
    /// <summary>
    /// Project ID that failed
    /// </summary>
    public Guid ProjectId { get; init; }
    
    /// <summary>
    /// Error message
    /// </summary>
    public required string ErrorMessage { get; init; }
    
    /// <summary>
    /// Exception type if applicable
    /// </summary>
    public string? ExceptionType { get; init; }
    
    /// <summary>
    /// Retry count
    /// </summary>
    public int RetryCount { get; init; }
    
    /// <summary>
    /// When the failure occurred
    /// </summary>
    public DateTime FailedAt { get; init; } = DateTime.UtcNow;
    
    /// <summary>
    /// Correlation ID for tracking
    /// </summary>
    public Guid CorrelationId { get; init; }
}
