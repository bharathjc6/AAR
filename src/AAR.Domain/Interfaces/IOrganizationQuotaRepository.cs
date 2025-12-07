// =============================================================================
// AAR.Domain - Interfaces/IOrganizationQuotaRepository.cs
// Repository interface for organization quotas
// =============================================================================

using AAR.Domain.Entities;

namespace AAR.Domain.Interfaces;

/// <summary>
/// Repository for organization quota operations
/// </summary>
public interface IOrganizationQuotaRepository
{
    /// <summary>
    /// Gets quota by organization ID
    /// </summary>
    Task<OrganizationQuota?> GetByOrganizationIdAsync(string organizationId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets or creates quota for an organization
    /// </summary>
    Task<OrganizationQuota> GetOrCreateAsync(string organizationId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Adds a new quota record
    /// </summary>
    Task AddAsync(OrganizationQuota quota, CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates an existing quota
    /// </summary>
    Task UpdateAsync(OrganizationQuota quota, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets organizations needing period reset
    /// </summary>
    Task<IReadOnlyList<OrganizationQuota>> GetNeedingPeriodResetAsync(CancellationToken cancellationToken = default);
}
