// =============================================================================
// AAR.Domain - Interfaces/IApiKeyRepository.cs
// Repository interface for ApiKey entities
// =============================================================================

using AAR.Domain.Entities;

namespace AAR.Domain.Interfaces;

/// <summary>
/// Repository interface for ApiKey entity operations
/// </summary>
public interface IApiKeyRepository
{
    /// <summary>
    /// Gets an API key by ID
    /// </summary>
    Task<ApiKey?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Gets an API key by its prefix (for lookup)
    /// </summary>
    Task<ApiKey?> GetByPrefixAsync(string prefix, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Gets all active API keys
    /// </summary>
    Task<IReadOnlyList<ApiKey>> GetActiveAsync(CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Finds an API key that matches the provided plain text key
    /// </summary>
    Task<ApiKey?> ValidateKeyAsync(string plainTextKey, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Adds a new API key
    /// </summary>
    Task<ApiKey> AddAsync(ApiKey apiKey, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Updates an API key
    /// </summary>
    Task UpdateAsync(ApiKey apiKey, CancellationToken cancellationToken = default);
}
