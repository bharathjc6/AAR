// =============================================================================
// AAR.Application - Configuration/RagProcessingOptions.cs
// Configuration options for RAG-based file processing and analysis routing
// =============================================================================

namespace AAR.Application.Configuration;

/// <summary>
/// Configuration options for RAG-based file processing and analysis routing.
/// Controls thresholds for direct send vs RAG chunking vs skip decisions.
/// </summary>
public sealed class RagProcessingOptions
{
    public const string SectionName = "RagProcessing";

    /// <summary>
    /// Files smaller than this threshold are sent directly to LLM (default: 10KB).
    /// These are small enough to fit in context without chunking overhead.
    /// </summary>
    public int DirectSendThresholdBytes { get; set; } = 10 * 1024;

    /// <summary>
    /// Files between DirectSendThreshold and this size use RAG chunking (default: 200KB).
    /// Files larger than this are skipped as likely generated/third-party.
    /// </summary>
    public int RagChunkThresholdBytes { get; set; } = 200 * 1024;

    /// <summary>
    /// Enterprise override to allow processing files larger than RagChunkThreshold.
    /// When enabled, large files are processed via RAG chunking instead of skipped.
    /// </summary>
    public bool AllowLargeFiles { get; set; } = false;

    /// <summary>
    /// Number of top-K high-risk files to prioritize for immediate analysis.
    /// </summary>
    public int RiskTopK { get; set; } = 20;

    /// <summary>
    /// Minimum risk score (0.0-1.0) to flag a file as high-risk for priority analysis.
    /// </summary>
    public float RiskThreshold { get; set; } = 0.7f;

    /// <summary>
    /// Maximum chunks to generate per file before skipping remainder.
    /// </summary>
    public int MaxChunksPerFile { get; set; } = 100;

    /// <summary>
    /// Maximum total chunks per job before stopping indexing.
    /// </summary>
    public int MaxTotalChunksPerJob { get; set; } = 10_000;

    /// <summary>
    /// Enable disk-backed chunk storage for very large repositories.
    /// When true, chunk text is written to temp files instead of held in memory.
    /// </summary>
    public bool UseDiskBackedChunks { get; set; } = true;

    /// <summary>
    /// Buffer size in KB for in-memory chunk processing before spilling to disk.
    /// </summary>
    public int ChunkBufferKB { get; set; } = 64;

    /// <summary>
    /// Queries used for RAG risk filtering to identify high-risk files.
    /// </summary>
    public List<string> RiskFilterQueries { get; set; } =
    [
        "authentication authorization login password credentials token jwt",
        "sql database query injection command execution",
        "file upload download path traversal directory access",
        "cryptography encryption hash validation secret key",
        "input validation sanitization xss csrf injection",
        "api endpoint controller handler request response",
        "configuration settings secrets environment variables",
        "error handling exception logging sensitive data"
    ];
}

/// <summary>
/// Configuration options for memory management and resource limits.
/// </summary>
public sealed class MemoryManagementOptions
{
    public const string SectionName = "MemoryManagement";

    /// <summary>
    /// Maximum worker memory in MB before triggering graceful pause (default: 4GB).
    /// </summary>
    public int MaxWorkerMemoryMB { get; set; } = 4096;

    /// <summary>
    /// Memory threshold percentage to start aggressive cleanup (default: 80%).
    /// </summary>
    public int MemoryWarningThresholdPercent { get; set; } = 80;

    /// <summary>
    /// Memory threshold percentage to pause processing (default: 90%).
    /// </summary>
    public int MemoryPauseThresholdPercent { get; set; } = 90;

    /// <summary>
    /// Interval in seconds to check memory usage during processing.
    /// </summary>
    public int MemoryCheckIntervalSeconds { get; set; } = 5;

    /// <summary>
    /// Minimum free disk space in MB required to start a job.
    /// </summary>
    public int MinFreeDiskSpaceMB { get; set; } = 1024;

    /// <summary>
    /// Maximum temp folder size per job in MB.
    /// </summary>
    public int MaxTempFolderSizeMB { get; set; } = 2048;

    /// <summary>
    /// Enable aggressive GC collection during batch processing.
    /// </summary>
    public bool EnableAggressiveGC { get; set; } = true;

    /// <summary>
    /// Number of batches between forced GC collections.
    /// </summary>
    public int GCIntervalBatches { get; set; } = 3;
}

/// <summary>
/// Configuration options for bounded concurrency.
/// </summary>
public sealed class ConcurrencyOptions
{
    public const string SectionName = "Concurrency";

    /// <summary>
    /// Maximum concurrent embedding API calls (default: 4).
    /// </summary>
    public int EmbeddingConcurrency { get; set; } = 4;

    /// <summary>
    /// Maximum concurrent reasoning/chat completion calls (default: 2).
    /// </summary>
    public int ReasoningConcurrency { get; set; } = 2;

    /// <summary>
    /// Maximum concurrent file reads during extraction (default: 8).
    /// </summary>
    public int FileReadConcurrency { get; set; } = 8;

    /// <summary>
    /// Batch size for embedding API calls (default: 32).
    /// </summary>
    public int EmbeddingBatchSize { get; set; } = 32;

    /// <summary>
    /// Maximum concurrent chunk processing operations.
    /// </summary>
    public int ChunkProcessingConcurrency { get; set; } = 4;
}

/// <summary>
/// Configuration options for job approval and cost warnings.
/// </summary>
public sealed class JobApprovalOptions
{
    public const string SectionName = "JobApproval";

    /// <summary>
    /// Token count threshold above which a warning is logged.
    /// </summary>
    public long WarnThresholdTokens { get; set; } = 500_000;

    /// <summary>
    /// Estimated cost threshold above which a warning is logged.
    /// </summary>
    public decimal WarnThresholdCost { get; set; } = 10.0m;

    /// <summary>
    /// Token count threshold above which approval is required.
    /// </summary>
    public long ApprovalThresholdTokens { get; set; } = 2_000_000;

    /// <summary>
    /// Estimated cost threshold above which approval is required.
    /// </summary>
    public decimal ApprovalThresholdCost { get; set; } = 50.0m;

    /// <summary>
    /// Whether to auto-reject jobs exceeding approval threshold (vs require manual approval).
    /// </summary>
    public bool AutoRejectOverThreshold { get; set; } = false;
}
