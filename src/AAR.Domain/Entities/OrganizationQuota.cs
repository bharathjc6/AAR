// =============================================================================
// AAR.Domain - Entities/OrganizationQuota.cs
// Entity for tracking per-organization quotas and usage
// =============================================================================

namespace AAR.Domain.Entities;

/// <summary>
/// Represents usage quotas for an organization/account
/// </summary>
public class OrganizationQuota : BaseEntity
{
    /// <summary>
    /// Organization identifier (can be API key ID for simple cases)
    /// </summary>
    public string OrganizationId { get; private set; } = string.Empty;

    /// <summary>
    /// Total credits allocated
    /// </summary>
    public decimal TotalCredits { get; private set; }

    /// <summary>
    /// Credits used this billing period
    /// </summary>
    public decimal CreditsUsed { get; private set; }

    /// <summary>
    /// Maximum concurrent jobs allowed
    /// </summary>
    public int MaxConcurrentJobs { get; private set; }

    /// <summary>
    /// Current active job count
    /// </summary>
    public int ActiveJobCount { get; private set; }

    /// <summary>
    /// Maximum storage in bytes
    /// </summary>
    public long MaxStorageBytes { get; private set; }

    /// <summary>
    /// Current storage used in bytes
    /// </summary>
    public long StorageUsedBytes { get; private set; }

    /// <summary>
    /// Tokens consumed in current period
    /// </summary>
    public long TokensConsumed { get; private set; }

    /// <summary>
    /// Maximum tokens per period
    /// </summary>
    public long MaxTokensPerPeriod { get; private set; }

    /// <summary>
    /// Billing period start
    /// </summary>
    public DateTime PeriodStartDate { get; private set; }

    /// <summary>
    /// Billing period end
    /// </summary>
    public DateTime PeriodEndDate { get; private set; }

    /// <summary>
    /// Whether the organization is suspended
    /// </summary>
    public bool IsSuspended { get; private set; }

    /// <summary>
    /// Suspension reason if suspended
    /// </summary>
    public string? SuspensionReason { get; private set; }

    private OrganizationQuota() { }

    public static OrganizationQuota CreateDefault(string organizationId)
    {
        var now = DateTime.UtcNow;
        return new OrganizationQuota
        {
            Id = Guid.NewGuid(),
            OrganizationId = organizationId,
            TotalCredits = 100m,
            CreditsUsed = 0m,
            MaxConcurrentJobs = 5,
            ActiveJobCount = 0,
            MaxStorageBytes = 10L * 1024 * 1024 * 1024, // 10GB
            StorageUsedBytes = 0,
            TokensConsumed = 0,
            MaxTokensPerPeriod = 10_000_000, // 10M tokens
            PeriodStartDate = new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc),
            PeriodEndDate = new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc).AddMonths(1),
            IsSuspended = false,
            CreatedAt = now
        };
    }

    public bool CanStartJob() => 
        !IsSuspended && 
        ActiveJobCount < MaxConcurrentJobs && 
        CreditsUsed < TotalCredits;

    public bool HasSufficientCredits(decimal estimatedCost) =>
        !IsSuspended && (CreditsUsed + estimatedCost) <= TotalCredits;

    public bool HasSufficientTokens(long estimatedTokens) =>
        !IsSuspended && (TokensConsumed + estimatedTokens) <= MaxTokensPerPeriod;

    public bool HasSufficientStorage(long requiredBytes) =>
        !IsSuspended && (StorageUsedBytes + requiredBytes) <= MaxStorageBytes;

    public void IncrementActiveJobs()
    {
        ActiveJobCount++;
        SetUpdated();
    }

    public void DecrementActiveJobs()
    {
        if (ActiveJobCount > 0)
            ActiveJobCount--;
        SetUpdated();
    }

    public void ConsumeCredits(decimal credits)
    {
        CreditsUsed += credits;
        SetUpdated();
    }

    public void ConsumeTokens(long tokens)
    {
        TokensConsumed += tokens;
        SetUpdated();
    }

    public void ConsumeStorage(long bytes)
    {
        StorageUsedBytes += bytes;
        SetUpdated();
    }

    public void ReleaseStorage(long bytes)
    {
        StorageUsedBytes = Math.Max(0, StorageUsedBytes - bytes);
        SetUpdated();
    }

    public void Suspend(string reason)
    {
        IsSuspended = true;
        SuspensionReason = reason;
        SetUpdated();
    }

    public void Unsuspend()
    {
        IsSuspended = false;
        SuspensionReason = null;
        SetUpdated();
    }

    public void ResetPeriod()
    {
        var now = DateTime.UtcNow;
        CreditsUsed = 0;
        TokensConsumed = 0;
        PeriodStartDate = new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        PeriodEndDate = PeriodStartDate.AddMonths(1);
        SetUpdated();
    }

    public decimal GetRemainingCredits() => Math.Max(0, TotalCredits - CreditsUsed);
    
    public long GetRemainingTokens() => Math.Max(0, MaxTokensPerPeriod - TokensConsumed);
    
    public long GetRemainingStorage() => Math.Max(0, MaxStorageBytes - StorageUsedBytes);
}
