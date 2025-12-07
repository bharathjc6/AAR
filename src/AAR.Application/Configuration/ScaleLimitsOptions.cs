// =============================================================================
// AAR.Application - Configuration/ScaleLimitsOptions.cs
// Configuration options for scaling limits and thresholds
// =============================================================================

namespace AAR.Application.Configuration;

/// <summary>
/// Configuration options for repository size limits and processing thresholds
/// </summary>
public sealed class ScaleLimitsOptions
{
    public const string SectionName = "ScaleLimits";

    /// <summary>
    /// Maximum uncompressed repository size in bytes (default: 500MB)
    /// </summary>
    public long MaxRepoUncompressedSizeBytes { get; set; } = 500 * 1024 * 1024;

    /// <summary>
    /// Maximum single file size in bytes (default: 10MB)
    /// </summary>
    public long MaxSingleFileSizeBytes { get; set; } = 10 * 1024 * 1024;

    /// <summary>
    /// Maximum number of files in a repository (default: 10000)
    /// </summary>
    public int MaxFilesCount { get; set; } = 10_000;

    /// <summary>
    /// Threshold below which repos are processed synchronously (default: 5MB)
    /// </summary>
    public long SynchronousProcessingThresholdBytes { get; set; } = 5 * 1024 * 1024;

    /// <summary>
    /// Maximum files for synchronous processing (default: 100)
    /// </summary>
    public int SynchronousProcessingMaxFiles { get; set; } = 100;

    /// <summary>
    /// Maximum job cost in credits before requiring approval (default: 100)
    /// </summary>
    public decimal MaxJobCostWithoutApproval { get; set; } = 100m;

    /// <summary>
    /// Cost per 1000 tokens for embedding (default: 0.0001)
    /// </summary>
    public decimal EmbeddingCostPer1000Tokens { get; set; } = 0.0001m;

    /// <summary>
    /// Cost per 1000 tokens for reasoning/analysis (default: 0.03)
    /// </summary>
    public decimal ReasoningCostPer1000Tokens { get; set; } = 0.03m;

    /// <summary>
    /// Estimated tokens per byte of source code (default: 0.25)
    /// </summary>
    public double EstimatedTokensPerByte { get; set; } = 0.25;
}

/// <summary>
/// Configuration options for embedding processing
/// </summary>
public sealed class EmbeddingProcessingOptions
{
    public const string SectionName = "EmbeddingProcessing";

    /// <summary>
    /// Number of chunks per embedding batch (default: 64)
    /// </summary>
    public int EmbeddingBatchSize { get; set; } = 64;

    /// <summary>
    /// Maximum concurrent embedding batches (default: 4)
    /// </summary>
    public int EmbeddingConcurrency { get; set; } = 4;

    /// <summary>
    /// Tokens per minute limit for embedding API (default: 150000)
    /// </summary>
    public int EmbeddingTokensPerMinute { get; set; } = 150_000;

    /// <summary>
    /// Requests per minute limit for embedding API (default: 3000)
    /// </summary>
    public int EmbeddingRequestsPerMinute { get; set; } = 3000;

    /// <summary>
    /// Circuit breaker failure threshold before opening (default: 5)
    /// </summary>
    public int CircuitBreakerFailureThreshold { get; set; } = 5;

    /// <summary>
    /// Circuit breaker break duration in seconds (default: 30)
    /// </summary>
    public int CircuitBreakerBreakDurationSeconds { get; set; } = 30;

    /// <summary>
    /// Maximum retry attempts for transient failures (default: 3)
    /// </summary>
    public int MaxRetryAttempts { get; set; } = 3;

    /// <summary>
    /// Base delay for exponential backoff in milliseconds (default: 1000)
    /// </summary>
    public int RetryBaseDelayMs { get; set; } = 1000;
}

/// <summary>
/// Configuration options for worker processing
/// </summary>
public sealed class WorkerProcessingOptions
{
    public const string SectionName = "WorkerProcessing";

    /// <summary>
    /// Maximum concurrent jobs per worker (default: 2)
    /// </summary>
    public int MaxConcurrentJobs { get; set; } = 2;

    /// <summary>
    /// Maximum concurrent file readers per job (default: 4)
    /// </summary>
    public int MaxConcurrentFileReaders { get; set; } = 4;

    /// <summary>
    /// Maximum in-memory buffer size in bytes (default: 50MB)
    /// </summary>
    public long MaxInMemoryBufferBytes { get; set; } = 50 * 1024 * 1024;

    /// <summary>
    /// Checkpoint interval - save progress every N files (default: 50)
    /// </summary>
    public int CheckpointIntervalFiles { get; set; } = 50;

    /// <summary>
    /// Checkpoint interval - save progress every N seconds (default: 30)
    /// </summary>
    public int CheckpointIntervalSeconds { get; set; } = 30;

    /// <summary>
    /// Minimum free disk space required in bytes (default: 1GB)
    /// </summary>
    public long MinFreeDiskSpaceBytes { get; set; } = 1024 * 1024 * 1024;

    /// <summary>
    /// Per-job disk quota in bytes (default: 2GB)
    /// </summary>
    public long PerJobDiskQuotaBytes { get; set; } = 2L * 1024 * 1024 * 1024;

    /// <summary>
    /// Dead letter threshold - max delivery attempts (default: 3)
    /// </summary>
    public int DeadLetterThreshold { get; set; } = 3;
}

/// <summary>
/// Configuration options for storage policies
/// </summary>
public sealed class StoragePolicyOptions
{
    public const string SectionName = "StoragePolicy";

    /// <summary>
    /// Default storage policy for new projects
    /// </summary>
    public StoragePolicyType DefaultPolicy { get; set; } = StoragePolicyType.StoreMetadataOnly;

    /// <summary>
    /// Whether to store chunk text content (default: false for privacy)
    /// </summary>
    public bool StoreChunkText { get; set; } = false;

    /// <summary>
    /// Retention period for completed projects in days (default: 90)
    /// </summary>
    public int RetentionDays { get; set; } = 90;

    /// <summary>
    /// Retention period for failed projects in days (default: 30)
    /// </summary>
    public int FailedProjectRetentionDays { get; set; } = 30;
}

/// <summary>
/// Storage policy types
/// </summary>
public enum StoragePolicyType
{
    /// <summary>Store full repository files</summary>
    StoreFullRepo,
    
    /// <summary>Store only metadata and embeddings</summary>
    StoreMetadataOnly,
    
    /// <summary>Store only embeddings (no source)</summary>
    StoreEmbeddingsOnly
}

/// <summary>
/// Configuration for resumable uploads
/// </summary>
public sealed class ResumableUploadOptions
{
    public const string SectionName = "ResumableUpload";

    /// <summary>
    /// Maximum part size in bytes (default: 10MB)
    /// </summary>
    public long MaxPartSizeBytes { get; set; } = 10 * 1024 * 1024;

    /// <summary>
    /// Minimum part size in bytes (default: 5MB)
    /// </summary>
    public long MinPartSizeBytes { get; set; } = 5 * 1024 * 1024;

    /// <summary>
    /// Maximum number of parts (default: 1000)
    /// </summary>
    public int MaxParts { get; set; } = 1000;

    /// <summary>
    /// Upload session timeout in minutes (default: 60)
    /// </summary>
    public int SessionTimeoutMinutes { get; set; } = 60;
}
