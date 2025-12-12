// =============================================================================
// AAR.Domain - Interfaces/IJobCheckpointRepository.cs
// Repository interface for job checkpoints
// =============================================================================

using AAR.Domain.Entities;

namespace AAR.Domain.Interfaces;

/// <summary>
/// Repository for job checkpoint operations
/// </summary>
public interface IJobCheckpointRepository
{
    /// <summary>
    /// Gets the latest checkpoint for a project
    /// </summary>
    Task<JobCheckpoint?> GetByProjectIdAsync(Guid projectId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets checkpoints by status
    /// </summary>
    Task<IReadOnlyList<JobCheckpoint>> GetByStatusAsync(CheckpointStatus status, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets checkpoints pending retry
    /// </summary>
    Task<IReadOnlyList<JobCheckpoint>> GetPendingRetryAsync(int maxRetries, CancellationToken cancellationToken = default);

    /// <summary>
    /// Adds a new checkpoint
    /// </summary>
    Task AddAsync(JobCheckpoint checkpoint, CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates an existing checkpoint
    /// </summary>
    Task UpdateAsync(JobCheckpoint checkpoint, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes checkpoints older than specified date
    /// </summary>
    Task DeleteOlderThanAsync(DateTime cutoffDate, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes all checkpoints for a project
    /// </summary>
    Task DeleteByProjectIdAsync(Guid projectId, CancellationToken cancellationToken = default);
}
