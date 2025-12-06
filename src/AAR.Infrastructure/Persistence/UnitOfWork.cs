// =============================================================================
// AAR.Infrastructure - Persistence/UnitOfWork.cs
// Unit of Work implementation for transaction management
// =============================================================================

using AAR.Domain.Interfaces;
using AAR.Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore.Storage;

namespace AAR.Infrastructure.Persistence;

/// <summary>
/// Unit of Work implementation that manages repositories and transactions
/// </summary>
public class UnitOfWork : IUnitOfWork
{
    private readonly AarDbContext _context;
    private IDbContextTransaction? _currentTransaction;
    private bool _disposed;

    private IProjectRepository? _projects;
    private IReportRepository? _reports;
    private IFileRecordRepository? _fileRecords;
    private IReviewFindingRepository? _reviewFindings;
    private IApiKeyRepository? _apiKeys;

    public UnitOfWork(AarDbContext context)
    {
        _context = context;
    }

    /// <inheritdoc/>
    public IProjectRepository Projects => 
        _projects ??= new ProjectRepository(_context);

    /// <inheritdoc/>
    public IReportRepository Reports => 
        _reports ??= new ReportRepository(_context);

    /// <inheritdoc/>
    public IFileRecordRepository FileRecords => 
        _fileRecords ??= new FileRecordRepository(_context);

    /// <inheritdoc/>
    public IReviewFindingRepository ReviewFindings => 
        _reviewFindings ??= new ReviewFindingRepository(_context);

    /// <inheritdoc/>
    public IApiKeyRepository ApiKeys => 
        _apiKeys ??= new ApiKeyRepository(_context);

    /// <inheritdoc/>
    public async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        return await _context.SaveChangesAsync(cancellationToken);
    }

    /// <inheritdoc/>
    public async Task BeginTransactionAsync(CancellationToken cancellationToken = default)
    {
        if (_currentTransaction is not null)
        {
            throw new InvalidOperationException("A transaction is already in progress.");
        }
        
        _currentTransaction = await _context.Database.BeginTransactionAsync(cancellationToken);
    }

    /// <inheritdoc/>
    public async Task CommitTransactionAsync(CancellationToken cancellationToken = default)
    {
        if (_currentTransaction is null)
        {
            throw new InvalidOperationException("No transaction is in progress.");
        }

        try
        {
            await _context.SaveChangesAsync(cancellationToken);
            await _currentTransaction.CommitAsync(cancellationToken);
        }
        finally
        {
            await _currentTransaction.DisposeAsync();
            _currentTransaction = null;
        }
    }

    /// <inheritdoc/>
    public async Task RollbackTransactionAsync(CancellationToken cancellationToken = default)
    {
        if (_currentTransaction is null)
        {
            throw new InvalidOperationException("No transaction is in progress.");
        }

        try
        {
            await _currentTransaction.RollbackAsync(cancellationToken);
        }
        finally
        {
            await _currentTransaction.DisposeAsync();
            _currentTransaction = null;
        }
    }

    /// <summary>
    /// Disposes the Unit of Work
    /// </summary>
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (!_disposed && disposing)
        {
            _currentTransaction?.Dispose();
            _context.Dispose();
        }
        _disposed = true;
    }
}
