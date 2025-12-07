// =============================================================================
// AAR.Infrastructure - Repositories/ChunkRepository.cs
// Repository implementation for Chunk entity
// =============================================================================

using AAR.Domain.Entities;
using AAR.Domain.Interfaces;
using AAR.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AAR.Infrastructure.Repositories;

/// <summary>
/// Repository for chunk operations
/// </summary>
public class ChunkRepository : IChunkRepository
{
    private readonly AarDbContext _context;

    public ChunkRepository(AarDbContext context)
    {
        _context = context;
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<Chunk>> GetByProjectIdAsync(
        Guid projectId, 
        CancellationToken cancellationToken = default)
    {
        return await _context.Chunks
            .Where(c => c.ProjectId == projectId)
            .ToListAsync(cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<Chunk?> GetByHashAsync(
        string chunkHash, 
        CancellationToken cancellationToken = default)
    {
        return await _context.Chunks
            .FirstOrDefaultAsync(c => c.ChunkHash == chunkHash, cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<Chunk>> GetByFilePathAsync(
        Guid projectId, 
        string filePath, 
        CancellationToken cancellationToken = default)
    {
        return await _context.Chunks
            .Where(c => c.ProjectId == projectId && c.FilePath == filePath)
            .OrderBy(c => c.StartLine)
            .ToListAsync(cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<Chunk>> GetWithEmbeddingsAsync(
        Guid projectId, 
        CancellationToken cancellationToken = default)
    {
        return await _context.Chunks
            .Where(c => c.ProjectId == projectId && c.EmbeddingJson != null)
            .ToListAsync(cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<Chunk>> GetWithoutEmbeddingsAsync(
        Guid projectId, 
        CancellationToken cancellationToken = default)
    {
        return await _context.Chunks
            .Where(c => c.ProjectId == projectId && c.EmbeddingJson == null)
            .ToListAsync(cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<Chunk> AddAsync(Chunk chunk, CancellationToken cancellationToken = default)
    {
        await _context.Chunks.AddAsync(chunk, cancellationToken);
        return chunk;
    }

    /// <inheritdoc/>
    public async Task AddRangeAsync(IEnumerable<Chunk> chunks, CancellationToken cancellationToken = default)
    {
        await _context.Chunks.AddRangeAsync(chunks, cancellationToken);
    }

    /// <inheritdoc/>
    public void Update(Chunk chunk)
    {
        _context.Chunks.Update(chunk);
    }

    /// <inheritdoc/>
    public async Task DeleteByProjectIdAsync(Guid projectId, CancellationToken cancellationToken = default)
    {
        await _context.Chunks
            .Where(c => c.ProjectId == projectId)
            .ExecuteDeleteAsync(cancellationToken);
    }

    /// <inheritdoc/>
    public async Task DeleteByFilePathAsync(
        Guid projectId, 
        string filePath, 
        CancellationToken cancellationToken = default)
    {
        await _context.Chunks
            .Where(c => c.ProjectId == projectId && c.FilePath == filePath)
            .ExecuteDeleteAsync(cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<bool> ExistsAsync(string chunkHash, CancellationToken cancellationToken = default)
    {
        return await _context.Chunks
            .AnyAsync(c => c.ChunkHash == chunkHash, cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<int> CountByProjectIdAsync(Guid projectId, CancellationToken cancellationToken = default)
    {
        return await _context.Chunks
            .CountAsync(c => c.ProjectId == projectId, cancellationToken);
    }
}
