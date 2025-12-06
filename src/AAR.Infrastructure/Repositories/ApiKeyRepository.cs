// =============================================================================
// AAR.Infrastructure - Repositories/ApiKeyRepository.cs
// Repository implementation for ApiKey entities
// =============================================================================

using AAR.Domain.Entities;
using AAR.Domain.Interfaces;
using AAR.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AAR.Infrastructure.Repositories;

/// <summary>
/// Repository implementation for ApiKey entities
/// </summary>
public class ApiKeyRepository : IApiKeyRepository
{
    private readonly AarDbContext _context;

    public ApiKeyRepository(AarDbContext context)
    {
        _context = context;
    }

    /// <inheritdoc/>
    public async Task<ApiKey?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await _context.ApiKeys
            .FirstOrDefaultAsync(k => k.Id == id, cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<ApiKey?> GetByPrefixAsync(string prefix, CancellationToken cancellationToken = default)
    {
        return await _context.ApiKeys
            .Where(k => k.KeyPrefix == prefix && k.IsActive)
            .FirstOrDefaultAsync(cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<ApiKey>> GetActiveAsync(CancellationToken cancellationToken = default)
    {
        return await _context.ApiKeys
            .Where(k => k.IsActive)
            .OrderBy(k => k.Name)
            .ToListAsync(cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<ApiKey?> ValidateKeyAsync(string plainTextKey, CancellationToken cancellationToken = default)
    {
        // Extract prefix from key (first 8 chars after "aar_")
        if (plainTextKey.Length < 12 || !plainTextKey.StartsWith("aar_"))
        {
            return null;
        }

        var prefix = plainTextKey[..8];

        // Find active keys with matching prefix
        var potentialKeys = await _context.ApiKeys
            .Where(k => k.IsActive && k.KeyPrefix == prefix)
            .ToListAsync(cancellationToken);

        // Validate against each potential key
        foreach (var key in potentialKeys)
        {
            if (key.ValidateKey(plainTextKey))
            {
                key.RecordUsage();
                return key;
            }
        }

        return null;
    }

    /// <inheritdoc/>
    public async Task<ApiKey> AddAsync(ApiKey apiKey, CancellationToken cancellationToken = default)
    {
        await _context.ApiKeys.AddAsync(apiKey, cancellationToken);
        return apiKey;
    }

    /// <inheritdoc/>
    public Task UpdateAsync(ApiKey apiKey, CancellationToken cancellationToken = default)
    {
        _context.ApiKeys.Update(apiKey);
        return Task.CompletedTask;
    }
}
