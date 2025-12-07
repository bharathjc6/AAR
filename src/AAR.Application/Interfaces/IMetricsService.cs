// =============================================================================
// AAR.Application - Interfaces/IMetricsService.cs
// Interface for application metrics and monitoring
// =============================================================================

namespace AAR.Application.Interfaces;

/// <summary>
/// Service for recording application metrics
/// </summary>
public interface IMetricsService
{
    /// <summary>
    /// Records a counter increment
    /// </summary>
    void IncrementCounter(string name, IDictionary<string, string>? tags = null);

    /// <summary>
    /// Records a gauge value
    /// </summary>
    void RecordGauge(string name, double value, IDictionary<string, string>? tags = null);

    /// <summary>
    /// Records a histogram value
    /// </summary>
    void RecordHistogram(string name, double value, IDictionary<string, string>? tags = null);

    /// <summary>
    /// Starts a timer for duration measurement
    /// </summary>
    IDisposable StartTimer(string name, IDictionary<string, string>? tags = null);

    /// <summary>
    /// Records a job start
    /// </summary>
    void RecordJobStart(Guid projectId, string jobType);

    /// <summary>
    /// Records a job completion
    /// </summary>
    void RecordJobCompletion(Guid projectId, string jobType, bool success, TimeSpan duration);

    /// <summary>
    /// Records tokens consumed
    /// </summary>
    void RecordTokensConsumed(string model, long tokens, string organizationId);

    /// <summary>
    /// Records queue metrics
    /// </summary>
    void RecordQueueMetrics(int queueLength, int deadLetterLength);

    /// <summary>
    /// Records memory usage
    /// </summary>
    void RecordMemoryUsage(long bytesUsed, long peakBytes);

    /// <summary>
    /// Records disk usage
    /// </summary>
    void RecordDiskUsage(string path, long freeBytes, long totalBytes);
}

/// <summary>
/// Predefined metric names
/// </summary>
public static class MetricNames
{
    public const string JobsQueued = "aar.jobs.queued";
    public const string JobsCompleted = "aar.jobs.completed";
    public const string JobsFailed = "aar.jobs.failed";
    public const string JobDuration = "aar.jobs.duration_seconds";
    public const string QueueLength = "aar.queue.length";
    public const string DeadLetterLength = "aar.queue.deadletter_length";
    public const string TokensConsumed = "aar.tokens.consumed";
    public const string EmbeddingsCreated = "aar.embeddings.created";
    public const string ChunksIndexed = "aar.chunks.indexed";
    public const string FilesProcessed = "aar.files.processed";
    public const string MemoryUsed = "aar.memory.used_bytes";
    public const string MemoryPeak = "aar.memory.peak_bytes";
    public const string DiskFree = "aar.disk.free_bytes";
    public const string DiskUsedPercent = "aar.disk.used_percent";
    public const string ApiRequestDuration = "aar.api.request_duration_seconds";
    public const string ApiRequestCount = "aar.api.request_count";
    public const string CircuitBreakerState = "aar.circuit_breaker.state";
    public const string RateLimitHits = "aar.rate_limit.hits";
    public const string CheckpointsSaved = "aar.checkpoints.saved";
    public const string CheckpointsResumed = "aar.checkpoints.resumed";
}

/// <summary>
/// Health check result
/// </summary>
public record HealthCheckResult
{
    public string Name { get; init; } = string.Empty;
    public bool IsHealthy { get; init; }
    public string? Message { get; init; }
    public TimeSpan Duration { get; init; }
    public Dictionary<string, object> Data { get; init; } = new();
}

/// <summary>
/// System health snapshot
/// </summary>
public record SystemHealthSnapshot
{
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;
    public int QueueLength { get; init; }
    public int DeadLetterQueueLength { get; init; }
    public int ActiveJobs { get; init; }
    public double AvgJobDurationSeconds { get; init; }
    public int JobFailuresLastHour { get; init; }
    public long TokensConsumedLastHour { get; init; }
    public long PeakMemoryBytes { get; init; }
    public double DiskFreePercent { get; init; }
    public bool IsHealthy { get; init; }
    public List<string> Alerts { get; init; } = new();
}
