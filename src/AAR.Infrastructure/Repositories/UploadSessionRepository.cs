// =============================================================================
// AAR.Infrastructure - Repositories/UploadSessionRepository.cs
// Repository implementation for upload sessions
// =============================================================================

using AAR.Domain.Entities;
using AAR.Domain.Interfaces;
using AAR.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AAR.Infrastructure.Repositories;

/// <summary>
/// EF Core implementation of upload session repository
/// </summary>
public sealed class UploadSessionRepository : IUploadSessionRepository
{
    private readonly AarDbContext _context;

    public UploadSessionRepository(AarDbContext context)
    {
        _context = context;
    }

    public async Task<UploadSession?> GetByIdAsync(
        Guid sessionId, 
        CancellationToken cancellationToken = default)
    {
        return await _context.Set<UploadSession>()
            .FirstOrDefaultAsync(s => s.Id == sessionId, cancellationToken);
    }

    public async Task<IReadOnlyList<UploadSession>> GetActiveByApiKeyAsync(
        Guid apiKeyId, 
        CancellationToken cancellationToken = default)
    {
        return await _context.Set<UploadSession>()
            .Where(s => s.ApiKeyId == apiKeyId && s.Status == UploadSessionStatus.InProgress)
            .OrderByDescending(s => s.CreatedAt)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<UploadSession>> GetExpiredSessionsAsync(
        CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        return await _context.Set<UploadSession>()
            .Where(s => s.Status == UploadSessionStatus.InProgress && s.ExpiresAt <= now)
            .ToListAsync(cancellationToken);
    }

    public async Task AddAsync(UploadSession session, CancellationToken cancellationToken = default)
    {
        await _context.Set<UploadSession>().AddAsync(session, cancellationToken);
    }

    public Task UpdateAsync(UploadSession session, CancellationToken cancellationToken = default)
    {
        _context.Set<UploadSession>().Update(session);
        return Task.CompletedTask;
    }

    public async Task DeleteAsync(Guid sessionId, CancellationToken cancellationToken = default)
    {
        var session = await GetByIdAsync(sessionId, cancellationToken);
        if (session != null)
        {
            _context.Set<UploadSession>().Remove(session);
        }
    }
}
