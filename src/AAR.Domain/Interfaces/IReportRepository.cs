// =============================================================================
// AAR.Domain - Interfaces/IReportRepository.cs
// Repository interface for Report entities
// =============================================================================

using AAR.Domain.Entities;

namespace AAR.Domain.Interfaces;

/// <summary>
/// Repository interface for Report entity operations
/// </summary>
public interface IReportRepository
{
    /// <summary>
    /// Gets a report by ID
    /// </summary>
    Task<Report?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Gets a report by project ID
    /// </summary>
    Task<Report?> GetByProjectIdAsync(Guid projectId, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Gets a report with all findings
    /// </summary>
    Task<Report?> GetWithFindingsAsync(Guid projectId, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Adds a new report
    /// </summary>
    Task<Report> AddAsync(Report report, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Updates a report
    /// </summary>
    Task UpdateAsync(Report report, CancellationToken cancellationToken = default);
}
