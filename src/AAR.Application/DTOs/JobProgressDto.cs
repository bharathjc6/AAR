// =============================================================================
// AAR.Application - DTOs/JobProgressDto.cs
// DTOs for job progress tracking and streaming
// =============================================================================

namespace AAR.Application.DTOs;

/// <summary>
/// Real-time job progress update
/// </summary>
public record JobProgressUpdate
{
    /// <summary>
    /// Project ID
    /// </summary>
    public Guid ProjectId { get; init; }

    /// <summary>
    /// Current processing phase
    /// </summary>
    public string Phase { get; init; } = string.Empty;

    /// <summary>
    /// Progress percentage (0-100)
    /// </summary>
    public double ProgressPercent { get; init; }

    /// <summary>
    /// Files processed
    /// </summary>
    public int FilesProcessed { get; init; }

    /// <summary>
    /// Total files
    /// </summary>
    public int TotalFiles { get; init; }

    /// <summary>
    /// Chunks indexed
    /// </summary>
    public int ChunksIndexed { get; init; }

    /// <summary>
    /// Embeddings created
    /// </summary>
    public int EmbeddingsCreated { get; init; }

    /// <summary>
    /// Tokens processed
    /// </summary>
    public long TokensProcessed { get; init; }

    /// <summary>
    /// Estimated remaining time in seconds
    /// </summary>
    public int? EstimatedRemainingSeconds { get; init; }

    /// <summary>
    /// Current file being processed
    /// </summary>
    public string? CurrentFile { get; init; }

    /// <summary>
    /// Timestamp
    /// </summary>
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;

    /// <summary>
    /// Status message
    /// </summary>
    public string? Message { get; init; }
}

/// <summary>
/// Partial finding update for streaming results
/// </summary>
public record PartialFindingUpdate
{
    /// <summary>
    /// Project ID
    /// </summary>
    public Guid ProjectId { get; init; }

    /// <summary>
    /// Finding details
    /// </summary>
    public FindingSummary Finding { get; init; } = new();

    /// <summary>
    /// Timestamp
    /// </summary>
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;

    /// <summary>
    /// Whether this is a final finding or preliminary
    /// </summary>
    public bool IsFinal { get; init; }
}

/// <summary>
/// Summary of a finding for streaming
/// </summary>
public record FindingSummary
{
    public Guid Id { get; init; }
    public string Severity { get; init; } = string.Empty;
    public string Category { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public string? FilePath { get; init; }
    public int? StartLine { get; init; }
    public int? EndLine { get; init; }
}

/// <summary>
/// Job completion notification
/// </summary>
public record JobCompletionUpdate
{
    /// <summary>
    /// Project ID
    /// </summary>
    public Guid ProjectId { get; init; }

    /// <summary>
    /// Whether job completed successfully
    /// </summary>
    public bool IsSuccess { get; init; }

    /// <summary>
    /// Final status
    /// </summary>
    public string Status { get; init; } = string.Empty;

    /// <summary>
    /// Report ID if successful
    /// </summary>
    public Guid? ReportId { get; init; }

    /// <summary>
    /// Error message if failed
    /// </summary>
    public string? ErrorMessage { get; init; }

    /// <summary>
    /// Total processing time in seconds
    /// </summary>
    public int ProcessingTimeSeconds { get; init; }

    /// <summary>
    /// Summary statistics
    /// </summary>
    public JobStatistics Statistics { get; init; } = new();
}

/// <summary>
/// Job statistics summary
/// </summary>
public record JobStatistics
{
    public int FilesProcessed { get; init; }
    public int ChunksIndexed { get; init; }
    public int EmbeddingsCreated { get; init; }
    public long TokensConsumed { get; init; }
    public int FindingsCount { get; init; }
    public int HighSeverityCount { get; init; }
    public int MediumSeverityCount { get; init; }
    public int LowSeverityCount { get; init; }
    public decimal CostCredits { get; init; }
}

/// <summary>
/// Job approval request
/// </summary>
public record JobApprovalRequest
{
    /// <summary>
    /// Project ID
    /// </summary>
    public Guid ProjectId { get; init; }

    /// <summary>
    /// Whether to approve (true) or reject (false)
    /// </summary>
    public bool Approve { get; init; }

    /// <summary>
    /// Optional comment
    /// </summary>
    public string? Comment { get; init; }
}

/// <summary>
/// Pending approval job info
/// </summary>
public record PendingApprovalJob
{
    public Guid ProjectId { get; init; }
    public string ProjectName { get; init; } = string.Empty;
    public string OrganizationId { get; init; } = string.Empty;
    public decimal EstimatedCost { get; init; }
    public long EstimatedTokens { get; init; }
    public int FileCount { get; init; }
    public long TotalSizeBytes { get; init; }
    public DateTime RequestedAt { get; init; }
    public string? RequestedBy { get; init; }
}
