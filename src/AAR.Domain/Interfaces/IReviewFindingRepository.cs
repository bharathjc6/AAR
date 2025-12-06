// =============================================================================
// AAR.Domain - Interfaces/IReviewFindingRepository.cs
// Repository interface for ReviewFinding entities
// =============================================================================

using AAR.Domain.Entities;
using AAR.Domain.Enums;

namespace AAR.Domain.Interfaces;

/// <summary>
/// Repository interface for ReviewFinding entity operations
/// </summary>
public interface IReviewFindingRepository
{
    /// <summary>
    /// Gets findings for a report
    /// </summary>
    Task<IReadOnlyList<ReviewFinding>> GetByReportIdAsync(Guid reportId, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Gets findings for a project
    /// </summary>
    Task<IReadOnlyList<ReviewFinding>> GetByProjectIdAsync(Guid projectId, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Gets findings filtered by severity
    /// </summary>
    Task<IReadOnlyList<ReviewFinding>> GetBySeverityAsync(Guid reportId, Severity severity, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Gets findings filtered by category
    /// </summary>
    Task<IReadOnlyList<ReviewFinding>> GetByCategoryAsync(Guid reportId, FindingCategory category, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Adds a single finding
    /// </summary>
    Task AddAsync(ReviewFinding finding, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Adds multiple findings
    /// </summary>
    Task AddRangeAsync(IEnumerable<ReviewFinding> findings, CancellationToken cancellationToken = default);
}
