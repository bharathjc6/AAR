// =============================================================================
// AAR.Infrastructure - Repositories/ReportRepository.cs
// Repository implementation for Report entities
// =============================================================================

using AAR.Domain.Entities;
using AAR.Domain.Interfaces;
using AAR.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AAR.Infrastructure.Repositories;

/// <summary>
/// Repository implementation for Report entities
/// </summary>
public class ReportRepository : IReportRepository
{
    private readonly AarDbContext _context;

    public ReportRepository(AarDbContext context)
    {
        _context = context;
    }

    /// <inheritdoc/>
    public async Task<Report?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await _context.Reports
            .FirstOrDefaultAsync(r => r.Id == id, cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<Report?> GetByProjectIdAsync(Guid projectId, CancellationToken cancellationToken = default)
    {
        return await _context.Reports
            .FirstOrDefaultAsync(r => r.ProjectId == projectId, cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<Report?> GetWithFindingsAsync(Guid projectId, CancellationToken cancellationToken = default)
    {
        return await _context.Reports
            .Include(r => r.Findings)
            .FirstOrDefaultAsync(r => r.ProjectId == projectId, cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<Report> AddAsync(Report report, CancellationToken cancellationToken = default)
    {
        await _context.Reports.AddAsync(report, cancellationToken);
        return report;
    }

    /// <inheritdoc/>
    public Task UpdateAsync(Report report, CancellationToken cancellationToken = default)
    {
        _context.Reports.Update(report);
        return Task.CompletedTask;
    }
}
