// =============================================================================
// AAR.Infrastructure - Repositories/OrganizationQuotaRepository.cs
// Repository implementation for organization quotas
// =============================================================================

using AAR.Domain.Entities;
using AAR.Domain.Interfaces;
using AAR.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AAR.Infrastructure.Repositories;

/// <summary>
/// EF Core implementation of organization quota repository
/// </summary>
public sealed class OrganizationQuotaRepository : IOrganizationQuotaRepository
{
    private readonly AarDbContext _context;

    public OrganizationQuotaRepository(AarDbContext context)
    {
        _context = context;
    }

    public async Task<OrganizationQuota?> GetByOrganizationIdAsync(
        string organizationId, 
        CancellationToken cancellationToken = default)
    {
        return await _context.Set<OrganizationQuota>()
            .FirstOrDefaultAsync(q => q.OrganizationId == organizationId, cancellationToken);
    }

    public async Task<OrganizationQuota> GetOrCreateAsync(
        string organizationId, 
        CancellationToken cancellationToken = default)
    {
        var quota = await GetByOrganizationIdAsync(organizationId, cancellationToken);
        
        if (quota is null)
        {
            quota = OrganizationQuota.CreateDefault(organizationId);
            await _context.Set<OrganizationQuota>().AddAsync(quota, cancellationToken);
            await _context.SaveChangesAsync(cancellationToken);
        }

        return quota;
    }

    public async Task AddAsync(OrganizationQuota quota, CancellationToken cancellationToken = default)
    {
        await _context.Set<OrganizationQuota>().AddAsync(quota, cancellationToken);
    }

    public Task UpdateAsync(OrganizationQuota quota, CancellationToken cancellationToken = default)
    {
        _context.Set<OrganizationQuota>().Update(quota);
        return Task.CompletedTask;
    }

    public async Task<IReadOnlyList<OrganizationQuota>> GetNeedingPeriodResetAsync(
        CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        return await _context.Set<OrganizationQuota>()
            .Where(q => q.PeriodEndDate <= now)
            .ToListAsync(cancellationToken);
    }
}
