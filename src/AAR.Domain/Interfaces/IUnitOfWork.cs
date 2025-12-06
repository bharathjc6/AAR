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
    /// Saves all changes to the database
    /// </summary>
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
    
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
}
