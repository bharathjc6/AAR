// =============================================================================
// AAR.Domain - Interfaces/IProjectRepository.cs
// Repository interface for Project entities
// =============================================================================

using AAR.Domain.Entities;
using AAR.Shared;

namespace AAR.Domain.Interfaces;

/// <summary>
/// Repository interface for Project entity operations
/// </summary>
public interface IProjectRepository
{
    /// <summary>
    /// Gets a project by ID
    /// </summary>
    Task<Project?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Gets a project with its files
    /// </summary>
    Task<Project?> GetWithFilesAsync(Guid id, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Gets a project with its report and findings
    /// </summary>
    Task<Project?> GetWithReportAsync(Guid id, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Gets paginated list of projects
    /// </summary>
    Task<PagedResult<Project>> GetPagedAsync(PaginationParams pagination, Guid? apiKeyId = null, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Adds a new project
    /// </summary>
    Task<Project> AddAsync(Project project, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Updates a project
    /// </summary>
    Task UpdateAsync(Project project, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Deletes a project
    /// </summary>
    Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);
}
