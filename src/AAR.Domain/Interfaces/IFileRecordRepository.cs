// =============================================================================
// AAR.Domain - Interfaces/IFileRecordRepository.cs
// Repository interface for FileRecord entities
// =============================================================================

using AAR.Domain.Entities;

namespace AAR.Domain.Interfaces;

/// <summary>
/// Repository interface for FileRecord entity operations
/// </summary>
public interface IFileRecordRepository
{
    /// <summary>
    /// Gets files for a project
    /// </summary>
    Task<IReadOnlyList<FileRecord>> GetByProjectIdAsync(Guid projectId, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Gets C# files for a project (for analysis)
    /// </summary>
    Task<IReadOnlyList<FileRecord>> GetCSharpFilesAsync(Guid projectId, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Adds multiple file records
    /// </summary>
    Task AddRangeAsync(IEnumerable<FileRecord> files, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Updates a file record
    /// </summary>
    Task UpdateAsync(FileRecord file, CancellationToken cancellationToken = default);
}
