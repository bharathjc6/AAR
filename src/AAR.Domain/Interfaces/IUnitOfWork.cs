// =============================================================================
// AAR.Domain - Interfaces/IUnitOfWork.cs
// Unit of Work pattern interface
// =============================================================================

namespace AAR.Domain.Interfaces;

/// <summary>
/// Unit of Work interface for transaction management
/// </summary>
public interface IUnitOfWork : IDisposable
{
    /// <summary>
    /// Project repository
    /// </summary>
    IProjectRepository Projects { get; }
    
    /// <summary>
    /// Report repository
    /// </summary>
    IReportRepository Reports { get; }
    
    /// <summary>
    /// File record repository
    /// </summary>
    IFileRecordRepository FileRecords { get; }
    
    /// <summary>
    /// Review finding repository
    /// </summary>
    IReviewFindingRepository ReviewFindings { get; }
    
    /// <summary>
    /// API key repository
    /// </summary>
    IApiKeyRepository ApiKeys { get; }

    /// <summary>
    /// Chunk repository for vector storage
    /// </summary>
    IChunkRepository Chunks { get; }

    /// <summary>
    /// Job checkpoint repository for resumable job state
    /// </summary>
    IJobCheckpointRepository JobCheckpoints { get; }
    
    /// <summary>
    /// Saves all changes to the database
    /// </summary>
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Clears the change tracker to free memory (useful for bulk operations)
    /// </summary>
    void ClearChangeTracker();
    
    /// <summary>
    /// Begins a new transaction
    /// </summary>
    Task BeginTransactionAsync(CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Commits the current transaction
    /// </summary>
    Task CommitTransactionAsync(CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Rolls back the current transaction
    /// </summary>
    Task RollbackTransactionAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Executes an operation within an execution strategy and transaction.
    /// This is required when using SqlServerRetryingExecutionStrategy.
    /// </summary>
    /// <typeparam name="TResult">The result type</typeparam>
    /// <param name="operation">The operation to execute</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The result of the operation</returns>
    Task<TResult> ExecuteInTransactionAsync<TResult>(
        Func<CancellationToken, Task<TResult>> operation,
        CancellationToken cancellationToken = default);
}
