// =============================================================================
// AAR.Domain - Entities/JobCheckpoint.cs
// Checkpoint entity for resumable job processing
// =============================================================================

namespace AAR.Domain.Entities;

/// <summary>
/// Represents a checkpoint for resumable job processing
/// </summary>
public class JobCheckpoint : BaseEntity
{
    /// <summary>
    /// The project/job this checkpoint belongs to
    /// </summary>
    public Guid ProjectId { get; private set; }

    /// <summary>
    /// Current processing phase
    /// </summary>
    public ProcessingPhase Phase { get; private set; }

    /// <summary>
    /// Index of the last successfully processed file
    /// </summary>
    public int LastProcessedFileIndex { get; private set; }

    /// <summary>
    /// Offset within the last processed chunk (for mid-file resume)
    /// </summary>
    public long LastProcessedChunkOffset { get; private set; }

    /// <summary>
    /// Total number of files discovered
    /// </summary>
    public int TotalFilesCount { get; private set; }

    /// <summary>
    /// Number of files processed so far
    /// </summary>
    public int FilesProcessedCount { get; private set; }

    /// <summary>
    /// Number of chunks indexed
    /// </summary>
    public int ChunksIndexedCount { get; private set; }

    /// <summary>
    /// Number of embeddings created
    /// </summary>
    public int EmbeddingsCreatedCount { get; private set; }

    /// <summary>
    /// Number of chunks skipped (deduplicated)
    /// </summary>
    public int ChunksSkippedCount { get; private set; }

    /// <summary>
    /// Total tokens processed
    /// </summary>
    public long TotalTokensProcessed { get; private set; }

    /// <summary>
    /// Estimated total tokens
    /// </summary>
    public long EstimatedTotalTokens { get; private set; }

    /// <summary>
    /// Checkpoint status
    /// </summary>
    public CheckpointStatus Status { get; private set; }

    /// <summary>
    /// Error message if failed
    /// </summary>
    public string? ErrorMessage { get; private set; }

    /// <summary>
    /// Number of retry attempts
    /// </summary>
    public int RetryCount { get; private set; }

    /// <summary>
    /// Last checkpoint timestamp
    /// </summary>
    public DateTime LastCheckpointAt { get; private set; }

    /// <summary>
    /// Processing started at
    /// </summary>
    public DateTime? ProcessingStartedAt { get; private set; }

    /// <summary>
    /// Processing completed at
    /// </summary>
    public DateTime? ProcessingCompletedAt { get; private set; }

    /// <summary>
    /// Serialized state for complex resume scenarios (JSON)
    /// </summary>
    public string? SerializedState { get; private set; }

    // Navigation property
    public Project? Project { get; private set; }

    private JobCheckpoint() { }

    public static JobCheckpoint Create(Guid projectId, int totalFiles, long estimatedTokens)
    {
        return new JobCheckpoint
        {
            Id = Guid.NewGuid(),
            ProjectId = projectId,
            Phase = ProcessingPhase.NotStarted,
            LastProcessedFileIndex = -1,
            LastProcessedChunkOffset = 0,
            TotalFilesCount = totalFiles,
            FilesProcessedCount = 0,
            ChunksIndexedCount = 0,
            EmbeddingsCreatedCount = 0,
            ChunksSkippedCount = 0,
            TotalTokensProcessed = 0,
            EstimatedTotalTokens = estimatedTokens,
            Status = CheckpointStatus.Pending,
            RetryCount = 0,
            LastCheckpointAt = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow
        };
    }

    public void StartProcessing()
    {
        Status = CheckpointStatus.InProgress;
        ProcessingStartedAt = DateTime.UtcNow;
        Phase = ProcessingPhase.Extracting;
        SetUpdated();
    }

    public void UpdateProgress(
        ProcessingPhase phase,
        int fileIndex,
        int filesProcessed,
        int chunksIndexed,
        int embeddingsCreated,
        long tokensProcessed)
    {
        Phase = phase;
        LastProcessedFileIndex = fileIndex;
        FilesProcessedCount = filesProcessed;
        ChunksIndexedCount = chunksIndexed;
        EmbeddingsCreatedCount = embeddingsCreated;
        TotalTokensProcessed = tokensProcessed;
        LastCheckpointAt = DateTime.UtcNow;
        SetUpdated();
    }

    public void IncrementChunksSkipped(int count = 1)
    {
        ChunksSkippedCount += count;
        SetUpdated();
    }

    public void SetSerializedState(string state)
    {
        SerializedState = state;
        SetUpdated();
    }

    public void MarkCompleted()
    {
        Status = CheckpointStatus.Completed;
        Phase = ProcessingPhase.Completed;
        ProcessingCompletedAt = DateTime.UtcNow;
        SetUpdated();
    }

    public void MarkFailed(string errorMessage)
    {
        Status = CheckpointStatus.Failed;
        ErrorMessage = errorMessage;
        RetryCount++;
        SetUpdated();
    }

    public void MarkForRetry()
    {
        Status = CheckpointStatus.PendingRetry;
        SetUpdated();
    }

    public void MarkDeadLettered()
    {
        Status = CheckpointStatus.DeadLettered;
        SetUpdated();
    }

    public bool CanRetry(int maxRetries) => RetryCount < maxRetries;

    public double GetProgressPercentage()
    {
        if (TotalFilesCount == 0) return 0;
        return (double)FilesProcessedCount / TotalFilesCount * 100;
    }

    public TimeSpan? GetEstimatedRemainingTime()
    {
        if (ProcessingStartedAt == null || FilesProcessedCount == 0) return null;
        
        var elapsed = DateTime.UtcNow - ProcessingStartedAt.Value;
        var remainingFiles = TotalFilesCount - FilesProcessedCount;
        var timePerFile = elapsed.TotalSeconds / FilesProcessedCount;
        
        return TimeSpan.FromSeconds(remainingFiles * timePerFile);
    }
}

/// <summary>
/// Processing phases for checkpointing
/// </summary>
public enum ProcessingPhase
{
    NotStarted = 0,
    Extracting = 1,
    Chunking = 2,
    Embedding = 3,
    Indexing = 4,
    Analyzing = 5,
    GeneratingReport = 6,
    Completed = 7
}

/// <summary>
/// Checkpoint status values
/// </summary>
public enum CheckpointStatus
{
    Pending = 0,
    InProgress = 1,
    Completed = 2,
    Failed = 3,
    PendingRetry = 4,
    DeadLettered = 5,
    RequiresApproval = 6,
    Approved = 7,
    Rejected = 8
}
