// =============================================================================
// AAR.Application - Interfaces/IJobQueueService.cs
// Interface for durable job queue abstraction
// =============================================================================

using AAR.Application.DTOs;

namespace AAR.Application.Interfaces;

/// <summary>
/// Priority levels for job queue
/// </summary>
public enum JobPriority
{
    Low = 0,
    Normal = 1,
    High = 2,
    Critical = 3
}

/// <summary>
/// Job queue message
/// </summary>
public record JobQueueMessage
{
    public Guid JobId { get; init; }
    public Guid ProjectId { get; init; }
    public string JobType { get; init; } = "Analysis";
    public JobPriority Priority { get; init; } = JobPriority.Normal;
    public int DeliveryCount { get; init; }
    public DateTime EnqueuedAt { get; init; } = DateTime.UtcNow;
    public DateTime? ScheduledFor { get; init; }
    public string? CorrelationId { get; init; }
    public Dictionary<string, string> Metadata { get; init; } = new();
}

/// <summary>
/// Durable job queue abstraction (supports Azure Queue, Service Bus, or in-memory)
/// </summary>
public interface IJobQueueService
{
    /// <summary>
    /// Enqueues a job for processing
    /// </summary>
    Task<string> EnqueueAsync(
        JobQueueMessage message,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Enqueues a job with delay
    /// </summary>
    Task<string> EnqueueWithDelayAsync(
        JobQueueMessage message,
        TimeSpan delay,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Dequeues the next job (for pull-based processing)
    /// </summary>
    Task<JobQueueMessage?> DequeueAsync(
        TimeSpan? visibilityTimeout = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Completes a job (removes from queue)
    /// </summary>
    Task CompleteAsync(string messageId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Abandons a job (returns to queue for retry)
    /// </summary>
    Task AbandonAsync(string messageId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Moves a job to dead-letter queue
    /// </summary>
    Task DeadLetterAsync(
        string messageId, 
        string reason,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets approximate queue length
    /// </summary>
    Task<int> GetQueueLengthAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets dead-letter queue length
    /// </summary>
    Task<int> GetDeadLetterQueueLengthAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Peeks at messages without removing
    /// </summary>
    Task<IReadOnlyList<JobQueueMessage>> PeekAsync(
        int count = 10,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Job progress notification service
/// </summary>
public interface IJobProgressService
{
    /// <summary>
    /// Reports job progress
    /// </summary>
    Task ReportProgressAsync(JobProgressUpdate progress, CancellationToken cancellationToken = default);

    /// <summary>
    /// Reports a partial finding
    /// </summary>
    Task ReportFindingAsync(PartialFindingUpdate finding, CancellationToken cancellationToken = default);

    /// <summary>
    /// Reports job completion
    /// </summary>
    Task ReportCompletionAsync(JobCompletionUpdate completion, CancellationToken cancellationToken = default);
}
