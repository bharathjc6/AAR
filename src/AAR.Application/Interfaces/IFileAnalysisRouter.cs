// =============================================================================
// AAR.Application - Interfaces/IFileAnalysisRouter.cs
// Interface for file analysis routing decisions
// =============================================================================

using AAR.Application.DTOs;

namespace AAR.Application.Interfaces;

/// <summary>
/// Routes files to appropriate analysis strategies based on size and content.
/// </summary>
public interface IFileAnalysisRouter
{
    /// <summary>
    /// Creates an analysis plan for a project, determining routing for each file.
    /// </summary>
    /// <param name="projectId">Project ID.</param>
    /// <param name="workingDirectory">Directory containing project files.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Complete analysis plan with file routing decisions.</returns>
    Task<ProjectAnalysisPlan> CreateAnalysisPlanAsync(
        Guid projectId,
        string workingDirectory,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Computes routing decision for a single file based on size thresholds.
    /// </summary>
    /// <param name="filePath">Path to the file.</param>
    /// <param name="fileSizeBytes">File size in bytes.</param>
    /// <returns>Routing decision and reason.</returns>
    (FileRoutingDecision Decision, string Reason) ComputeRoutingDecision(
        string filePath,
        long fileSizeBytes);

    /// <summary>
    /// Estimates analysis metrics without actually processing files.
    /// </summary>
    /// <param name="workingDirectory">Directory containing project files.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Analysis estimation for preflight response.</returns>
    Task<AnalysisEstimation> EstimateAnalysisAsync(
        string workingDirectory,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Service for RAG-based risk filtering of files.
/// </summary>
public interface IRagRiskFilter
{
    /// <summary>
    /// Computes risk scores for files using RAG similarity to known risk patterns.
    /// </summary>
    /// <param name="projectId">Project ID (must be indexed first).</param>
    /// <param name="filePaths">Files to score.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Dictionary of file paths to risk scores.</returns>
    Task<Dictionary<string, float>> ComputeRiskScoresAsync(
        Guid projectId,
        IEnumerable<string> filePaths,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets top-K high-risk files from the project.
    /// </summary>
    /// <param name="projectId">Project ID.</param>
    /// <param name="topK">Number of files to return.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>List of high-risk file paths with scores.</returns>
    Task<List<(string FilePath, float RiskScore)>> GetHighRiskFilesAsync(
        Guid projectId,
        int topK,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Service for disk-backed chunk storage.
/// </summary>
public interface ITempFileChunkWriter
{
    /// <summary>
    /// Writes chunk content to a temp file and returns the path.
    /// </summary>
    /// <param name="jobId">Job/project ID for organizing temp files.</param>
    /// <param name="chunkId">Unique chunk identifier.</param>
    /// <param name="content">Chunk text content.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Path to the temp file containing chunk content.</returns>
    Task<string> WriteChunkAsync(
        Guid jobId,
        string chunkId,
        string content,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads chunk content from a temp file.
    /// </summary>
    /// <param name="tempFilePath">Path to the temp file.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Chunk text content.</returns>
    Task<string> ReadChunkAsync(
        string tempFilePath,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a stream reader for a chunk file (for streaming processing).
    /// </summary>
    /// <param name="tempFilePath">Path to the temp file.</param>
    /// <returns>Stream reader for the chunk.</returns>
    StreamReader GetChunkReader(string tempFilePath);

    /// <summary>
    /// Cleans up all temp files for a job.
    /// </summary>
    /// <param name="jobId">Job/project ID.</param>
    void CleanupJob(Guid jobId);

    /// <summary>
    /// Gets total disk space used by a job's temp files.
    /// </summary>
    /// <param name="jobId">Job/project ID.</param>
    /// <returns>Total bytes used.</returns>
    long GetJobDiskUsage(Guid jobId);

    /// <summary>
    /// Checks if sufficient disk space is available.
    /// </summary>
    /// <param name="requiredBytes">Required bytes.</param>
    /// <returns>True if sufficient space available.</returns>
    bool HasSufficientDiskSpace(long requiredBytes);
}

/// <summary>
/// Service for memory monitoring and management.
/// </summary>
public interface IMemoryMonitor
{
    /// <summary>
    /// Gets current memory usage in MB.
    /// </summary>
    long CurrentMemoryMB { get; }

    /// <summary>
    /// Gets memory usage as percentage of configured maximum.
    /// </summary>
    int MemoryUsagePercent { get; }

    /// <summary>
    /// Whether memory usage is above warning threshold.
    /// </summary>
    bool IsMemoryWarning { get; }

    /// <summary>
    /// Whether memory usage is above pause threshold.
    /// </summary>
    bool ShouldPauseProcessing { get; }

    /// <summary>
    /// Requests garbage collection if memory is high.
    /// </summary>
    void RequestGCIfNeeded();

    /// <summary>
    /// Forces aggressive garbage collection.
    /// </summary>
    void ForceAggressiveGC();

    /// <summary>
    /// Records a memory usage sample for telemetry.
    /// </summary>
    void RecordMemorySample();
}

/// <summary>
/// Service for bounded concurrency control.
/// </summary>
public interface IConcurrencyLimiter
{
    /// <summary>
    /// Acquires a slot for embedding operations.
    /// </summary>
    Task<IDisposable> AcquireEmbeddingSlotAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Acquires a slot for reasoning/chat operations.
    /// </summary>
    Task<IDisposable> AcquireReasoningSlotAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Acquires a slot for file read operations.
    /// </summary>
    Task<IDisposable> AcquireFileReadSlotAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets current embedding queue depth.
    /// </summary>
    int EmbeddingQueueDepth { get; }

    /// <summary>
    /// Gets current reasoning queue depth.
    /// </summary>
    int ReasoningQueueDepth { get; }
}
