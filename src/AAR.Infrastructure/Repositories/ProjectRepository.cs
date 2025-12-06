// =============================================================================
// AAR.Infrastructure - Repositories/ProjectRepository.cs
// Repository implementation for Project entities
// =============================================================================

using AAR.Domain.Entities;
using AAR.Domain.Interfaces;
using AAR.Infrastructure.Persistence;
using AAR.Shared;
using Microsoft.EntityFrameworkCore;

namespace AAR.Infrastructure.Repositories;

/// <summary>
/// Repository implementation for Project entities
/// </summary>
public class ProjectRepository : IProjectRepository
{
    private readonly AarDbContext _context;

    public ProjectRepository(AarDbContext context)
    {
        _context = context;
    }

    /// <inheritdoc/>
    public async Task<Project?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await _context.Projects
            .FirstOrDefaultAsync(p => p.Id == id, cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<Project?> GetWithFilesAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await _context.Projects
            .Include(p => p.Files)
            .FirstOrDefaultAsync(p => p.Id == id, cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<Project?> GetWithReportAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await _context.Projects
            .Include(p => p.Report)
            .FirstOrDefaultAsync(p => p.Id == id, cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<PagedResult<Project>> GetPagedAsync(
        PaginationParams pagination, 
        Guid? apiKeyId = null,
        CancellationToken cancellationToken = default)
    {
        var query = _context.Projects.AsQueryable();

        if (apiKeyId.HasValue)
        {
            query = query.Where(p => p.ApiKeyId == apiKeyId.Value);
        }

        var totalCount = await query.CountAsync(cancellationToken);

        var items = await query
            .Include(p => p.Report)
            .OrderByDescending(p => p.CreatedAt)
            .Skip(pagination.Skip)
            .Take(pagination.PageSize)
            .ToListAsync(cancellationToken);

        return new PagedResult<Project>(items, totalCount, pagination.Page, pagination.PageSize);
    }

    /// <inheritdoc/>
    public async Task<Project> AddAsync(Project project, CancellationToken cancellationToken = default)
    {
        await _context.Projects.AddAsync(project, cancellationToken);
        return project;
    }

    /// <inheritdoc/>
    public Task UpdateAsync(Project project, CancellationToken cancellationToken = default)
    {
        _context.Projects.Update(project);
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var project = await GetByIdAsync(id, cancellationToken);
        if (project is not null)
        {
            _context.Projects.Remove(project);
        }
    }
}
