// =============================================================================
// AAR.Shared - KeyVault/IKeyVaultSecretProvider.cs
// Abstraction for retrieving secrets from Azure Key Vault or mock providers
// =============================================================================

namespace AAR.Shared.KeyVault;

/// <summary>
/// Abstraction for retrieving secrets. Implementations can use Azure Key Vault,
/// mock providers for testing, or local development providers.
/// </summary>
public interface IKeyVaultSecretProvider
{
    /// <summary>
    /// Retrieves a secret value by name.
    /// </summary>
    /// <param name="secretName">The name of the secret (uses '--' as hierarchy delimiter)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The secret value, or null if not found</returns>
    Task<string?> GetSecretAsync(string secretName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves multiple secrets by their names.
    /// </summary>
    /// <param name="secretNames">The names of the secrets to retrieve</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Dictionary of secret names to values (null values for not found)</returns>
    Task<IDictionary<string, string?>> GetSecretsAsync(
        IEnumerable<string> secretNames, 
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks if the provider is available and configured.
    /// </summary>
    bool IsAvailable { get; }

    /// <summary>
    /// Gets the provider name for logging purposes.
    /// </summary>
    string ProviderName { get; }
}
