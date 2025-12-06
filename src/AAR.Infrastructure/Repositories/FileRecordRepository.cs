// =============================================================================
// AAR.Infrastructure - Repositories/FileRecordRepository.cs
// Repository implementation for FileRecord entities
// =============================================================================

using AAR.Domain.Entities;
using AAR.Domain.Interfaces;
using AAR.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AAR.Infrastructure.Repositories;

/// <summary>
/// Repository implementation for FileRecord entities
/// </summary>
public class FileRecordRepository : IFileRecordRepository
{
    private readonly AarDbContext _context;

    public FileRecordRepository(AarDbContext context)
    {
        _context = context;
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<FileRecord>> GetByProjectIdAsync(
        Guid projectId, 
        CancellationToken cancellationToken = default)
    {
        return await _context.FileRecords
            .Where(f => f.ProjectId == projectId)
            .OrderBy(f => f.RelativePath)
            .ToListAsync(cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<FileRecord>> GetCSharpFilesAsync(
        Guid projectId, 
        CancellationToken cancellationToken = default)
    {
        return await _context.FileRecords
            .Where(f => f.ProjectId == projectId && f.Extension == ".cs")
            .OrderBy(f => f.RelativePath)
            .ToListAsync(cancellationToken);
    }

    /// <inheritdoc/>
    public async Task AddRangeAsync(
        IEnumerable<FileRecord> files, 
        CancellationToken cancellationToken = default)
    {
        await _context.FileRecords.AddRangeAsync(files, cancellationToken);
    }

    /// <inheritdoc/>
    public Task UpdateAsync(FileRecord file, CancellationToken cancellationToken = default)
    {
        _context.FileRecords.Update(file);
        return Task.CompletedTask;
    }
}
