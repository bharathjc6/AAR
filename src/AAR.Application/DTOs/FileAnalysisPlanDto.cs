// =============================================================================
// AAR.Application - DTOs/FileAnalysisPlan.cs
// DTOs for file analysis routing decisions
// =============================================================================

namespace AAR.Application.DTOs;

/// <summary>
/// Represents the analysis plan for a single file.
/// </summary>
public record FileAnalysisPlan
{
    /// <summary>
    /// Relative file path from project root.
    /// </summary>
    public required string FilePath { get; init; }

    /// <summary>
    /// Full file path on disk.
    /// </summary>
    public required string FullPath { get; init; }

    /// <summary>
    /// File size in bytes.
    /// </summary>
    public long FileSizeBytes { get; init; }

    /// <summary>
    /// Routing decision for this file.
    /// </summary>
    public FileRoutingDecision Decision { get; init; }

    /// <summary>
    /// Reason for the routing decision (for logging/reporting).
    /// </summary>
    public string? DecisionReason { get; init; }

    /// <summary>
    /// Risk score from RAG filter (0.0-1.0, higher = riskier).
    /// </summary>
    public float RiskScore { get; init; }

    /// <summary>
    /// Whether this file is flagged as high-risk for priority analysis.
    /// </summary>
    public bool IsHighRisk { get; init; }

    /// <summary>
    /// Estimated token count for this file.
    /// </summary>
    public int EstimatedTokens { get; init; }

    /// <summary>
    /// Number of chunks created for this file (for RAG routing).
    /// </summary>
    public int ChunkCount { get; init; }

    /// <summary>
    /// Detected programming language.
    /// </summary>
    public string? Language { get; init; }

    /// <summary>
    /// Path to temp file containing chunk data (if using disk-backed chunks).
    /// </summary>
    public string? TempChunkFilePath { get; init; }
}

/// <summary>
/// File routing decisions for analysis.
/// </summary>
public enum FileRoutingDecision
{
    /// <summary>
    /// Send entire file content directly to LLM (small files).
    /// </summary>
    DirectSend = 0,

    /// <summary>
    /// Use RAG chunking and retrieval for analysis.
    /// </summary>
    RagChunks = 1,

    /// <summary>
    /// Skip this file entirely.
    /// </summary>
    Skipped = 2
}

/// <summary>
/// Reason codes for skipping files.
/// </summary>
public static class SkipReasonCodes
{
    public const string TooLarge = "skipped_large_file";
    public const string TooManyChunks = "skipped_too_many_chunks";
    public const string BinaryFile = "skipped_binary_file";
    public const string ExcludedPath = "skipped_excluded_path";
    public const string ReadError = "skipped_read_error";
    public const string EncodingError = "skipped_encoding_error";
}

/// <summary>
/// Aggregated analysis plan for a project.
/// </summary>
public record ProjectAnalysisPlan
{
    /// <summary>
    /// Project ID.
    /// </summary>
    public Guid ProjectId { get; init; }

    /// <summary>
    /// Working directory containing project files.
    /// </summary>
    public required string WorkingDirectory { get; init; }

    /// <summary>
    /// Individual file plans.
    /// </summary>
    public required List<FileAnalysisPlan> Files { get; init; }

    /// <summary>
    /// High-risk files to analyze first.
    /// </summary>
    public List<FileAnalysisPlan> HighRiskFiles => Files
        .Where(f => f.IsHighRisk && f.Decision != FileRoutingDecision.Skipped)
        .OrderByDescending(f => f.RiskScore)
        .ToList();

    /// <summary>
    /// Normal priority files to analyze after high-risk.
    /// </summary>
    public List<FileAnalysisPlan> NormalPriorityFiles => Files
        .Where(f => !f.IsHighRisk && f.Decision != FileRoutingDecision.Skipped)
        .ToList();

    /// <summary>
    /// Count of files for direct send.
    /// </summary>
    public int DirectSendCount => Files.Count(f => f.Decision == FileRoutingDecision.DirectSend);

    /// <summary>
    /// Count of files for RAG chunking.
    /// </summary>
    public int RagChunkCount => Files.Count(f => f.Decision == FileRoutingDecision.RagChunks);

    /// <summary>
    /// Count of skipped files.
    /// </summary>
    public int SkippedCount => Files.Count(f => f.Decision == FileRoutingDecision.Skipped);

    /// <summary>
    /// Total estimated tokens.
    /// </summary>
    public long EstimatedTotalTokens => Files.Sum(f => (long)f.EstimatedTokens);

    /// <summary>
    /// Total file size in bytes.
    /// </summary>
    public long TotalFileSizeBytes => Files.Sum(f => f.FileSizeBytes);

    /// <summary>
    /// Total chunk count across all files.
    /// </summary>
    public int TotalChunkCount => Files.Sum(f => f.ChunkCount);

    /// <summary>
    /// When the plan was created.
    /// </summary>
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;

    /// <summary>
    /// Serializes the plan to JSON for storage.
    /// </summary>
    public string ToJson() => System.Text.Json.JsonSerializer.Serialize(this);

    /// <summary>
    /// Deserializes a plan from JSON.
    /// </summary>
    public static ProjectAnalysisPlan? FromJson(string json) =>
        System.Text.Json.JsonSerializer.Deserialize<ProjectAnalysisPlan>(json);
}

/// <summary>
/// Summary statistics for preflight estimation.
/// </summary>
public record AnalysisEstimation
{
    /// <summary>
    /// Count of files that will use direct send.
    /// </summary>
    public int DirectSendCount { get; init; }

    /// <summary>
    /// Count of files that will use RAG chunking.
    /// </summary>
    public int RagChunkCount { get; init; }

    /// <summary>
    /// Count of files that will be skipped.
    /// </summary>
    public int SkippedCount { get; init; }

    /// <summary>
    /// Estimated total tokens for processing.
    /// </summary>
    public long EstimatedTokens { get; init; }

    /// <summary>
    /// Estimated cost in USD.
    /// </summary>
    public decimal EstimatedCost { get; init; }

    /// <summary>
    /// Estimated processing time in seconds.
    /// </summary>
    public int EstimatedProcessingTimeSeconds { get; init; }

    /// <summary>
    /// Whether approval is required for this job.
    /// </summary>
    public bool RequiresApproval { get; init; }

    /// <summary>
    /// Warnings about the analysis.
    /// </summary>
    public List<string> Warnings { get; init; } = [];

    /// <summary>
    /// Breakdown by file type.
    /// </summary>
    public Dictionary<string, int> FileTypeBreakdown { get; init; } = [];

    /// <summary>
    /// List of skipped files with reasons.
    /// </summary>
    public List<SkippedFileInfo> SkippedFiles { get; init; } = [];
}

/// <summary>
/// Information about a skipped file.
/// </summary>
public record SkippedFileInfo
{
    /// <summary>
    /// Relative file path.
    /// </summary>
    public required string FilePath { get; init; }

    /// <summary>
    /// File size in bytes.
    /// </summary>
    public long FileSizeBytes { get; init; }

    /// <summary>
    /// Reason the file was skipped.
    /// </summary>
    public required string Reason { get; init; }

    /// <summary>
    /// Reason code for programmatic access.
    /// </summary>
    public required string ReasonCode { get; init; }
}
