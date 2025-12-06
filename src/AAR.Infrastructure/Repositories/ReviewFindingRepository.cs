// =============================================================================
// AAR.Infrastructure - Repositories/ReviewFindingRepository.cs
// Repository implementation for ReviewFinding entities
// =============================================================================

using AAR.Domain.Entities;
using AAR.Domain.Enums;
using AAR.Domain.Interfaces;
using AAR.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AAR.Infrastructure.Repositories;

/// <summary>
/// Repository implementation for ReviewFinding entities
/// </summary>
public class ReviewFindingRepository : IReviewFindingRepository
{
    private readonly AarDbContext _context;

    public ReviewFindingRepository(AarDbContext context)
    {
        _context = context;
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<ReviewFinding>> GetByReportIdAsync(
        Guid reportId, 
        CancellationToken cancellationToken = default)
    {
        return await _context.ReviewFindings
            .Where(f => f.ReportId == reportId)
            .OrderByDescending(f => f.Severity)
            .ThenBy(f => f.FilePath)
            .ToListAsync(cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<ReviewFinding>> GetByProjectIdAsync(
        Guid projectId, 
        CancellationToken cancellationToken = default)
    {
        return await _context.ReviewFindings
            .Where(f => f.ProjectId == projectId)
            .OrderByDescending(f => f.Severity)
            .ThenBy(f => f.FilePath)
            .ToListAsync(cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<ReviewFinding>> GetBySeverityAsync(
        Guid reportId, 
        Severity severity, 
        CancellationToken cancellationToken = default)
    {
        return await _context.ReviewFindings
            .Where(f => f.ReportId == reportId && f.Severity == severity)
            .OrderBy(f => f.FilePath)
            .ToListAsync(cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<ReviewFinding>> GetByCategoryAsync(
        Guid reportId, 
        FindingCategory category, 
        CancellationToken cancellationToken = default)
    {
        return await _context.ReviewFindings
            .Where(f => f.ReportId == reportId && f.Category == category)
            .OrderByDescending(f => f.Severity)
            .ToListAsync(cancellationToken);
    }

    /// <inheritdoc/>
    public async Task AddAsync(
        ReviewFinding finding, 
        CancellationToken cancellationToken = default)
    {
        await _context.ReviewFindings.AddAsync(finding, cancellationToken);
    }

    /// <inheritdoc/>
    public async Task AddRangeAsync(
        IEnumerable<ReviewFinding> findings, 
        CancellationToken cancellationToken = default)
    {
        await _context.ReviewFindings.AddRangeAsync(findings, cancellationToken);
    }
}
