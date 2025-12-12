// =============================================================================
// AAR.Infrastructure - Repositories/JobCheckpointRepository.cs
// Repository implementation for job checkpoints
// =============================================================================

using AAR.Domain.Entities;
using AAR.Domain.Interfaces;
using AAR.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AAR.Infrastructure.Repositories;

/// <summary>
/// EF Core implementation of job checkpoint repository
/// </summary>
public sealed class JobCheckpointRepository : IJobCheckpointRepository
{
    private readonly AarDbContext _context;

    public JobCheckpointRepository(AarDbContext context)
    {
        _context = context;
    }

    public async Task<JobCheckpoint?> GetByProjectIdAsync(
        Guid projectId, 
        CancellationToken cancellationToken = default)
    {
        return await _context.Set<JobCheckpoint>()
            .Where(c => c.ProjectId == projectId)
            .OrderByDescending(c => c.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<JobCheckpoint>> GetByStatusAsync(
        CheckpointStatus status, 
        CancellationToken cancellationToken = default)
    {
        return await _context.Set<JobCheckpoint>()
            .Where(c => c.Status == status)
            .OrderBy(c => c.CreatedAt)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<JobCheckpoint>> GetPendingRetryAsync(
        int maxRetries, 
        CancellationToken cancellationToken = default)
    {
        return await _context.Set<JobCheckpoint>()
            .Where(c => c.Status == CheckpointStatus.PendingRetry && c.RetryCount < maxRetries)
            .OrderBy(c => c.LastCheckpointAt)
            .ToListAsync(cancellationToken);
    }

    public async Task AddAsync(JobCheckpoint checkpoint, CancellationToken cancellationToken = default)
    {
        await _context.Set<JobCheckpoint>().AddAsync(checkpoint, cancellationToken);
    }

    public Task UpdateAsync(JobCheckpoint checkpoint, CancellationToken cancellationToken = default)
    {
        _context.Set<JobCheckpoint>().Update(checkpoint);
        return Task.CompletedTask;
    }

    public async Task DeleteOlderThanAsync(DateTime cutoffDate, CancellationToken cancellationToken = default)
    {
        var oldCheckpoints = await _context.Set<JobCheckpoint>()
            .Where(c => c.CreatedAt < cutoffDate && 
                        (c.Status == CheckpointStatus.Completed || c.Status == CheckpointStatus.DeadLettered))
            .ToListAsync(cancellationToken);

        _context.Set<JobCheckpoint>().RemoveRange(oldCheckpoints);
    }

    public async Task DeleteByProjectIdAsync(Guid projectId, CancellationToken cancellationToken = default)
    {
        var checkpoints = await _context.Set<JobCheckpoint>()
            .Where(c => c.ProjectId == projectId)
            .ToListAsync(cancellationToken);

        _context.Set<JobCheckpoint>().RemoveRange(checkpoints);
    }
}
