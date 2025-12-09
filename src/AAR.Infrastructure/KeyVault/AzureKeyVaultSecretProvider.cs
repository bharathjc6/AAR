// =============================================================================
// AAR.Infrastructure - KeyVault/AzureKeyVaultSecretProvider.cs
// Azure Key Vault implementation using DefaultAzureCredential
// =============================================================================

using AAR.Shared.KeyVault;
using Azure;
using Azure.Core;
using Azure.Identity;
using Azure.Security.KeyVault.Secrets;
using Microsoft.Extensions.Logging;

namespace AAR.Infrastructure.KeyVault;

/// <summary>
/// Azure Key Vault implementation using DefaultAzureCredential.
/// Supports Managed Identity in Azure and various local development credentials.
/// </summary>
public class AzureKeyVaultSecretProvider : IKeyVaultSecretProvider
{
    private readonly SecretClient? _client;
    private readonly ILogger<AzureKeyVaultSecretProvider> _logger;
    private readonly KeyVaultOptions _options;
    private readonly bool _isAvailable;

    public AzureKeyVaultSecretProvider(
        KeyVaultOptions options,
        ILogger<AzureKeyVaultSecretProvider> logger)
    {
        _options = options;
        _logger = logger;

        if (string.IsNullOrEmpty(options.VaultUri))
        {
            _logger.LogWarning("Key Vault URI not configured. Azure Key Vault provider is unavailable.");
            _isAvailable = false;
            return;
        }

        try
        {
            // DefaultAzureCredential tries the following in order:
            // 1. Environment variables (AZURE_CLIENT_ID, AZURE_CLIENT_SECRET, AZURE_TENANT_ID)
            // 2. Managed Identity (when running in Azure)
            // 3. Visual Studio credentials
            // 4. Azure CLI credentials (az login)
            // 5. Azure PowerShell credentials
            var credential = new DefaultAzureCredential(new DefaultAzureCredentialOptions
            {
                ExcludeInteractiveBrowserCredential = true, // Don't prompt in production
                Retry =
                {
                    MaxRetries = 3,
                    Delay = TimeSpan.FromSeconds(1),
                    MaxDelay = TimeSpan.FromSeconds(10)
                }
            });

            _client = new SecretClient(
                new Uri(options.VaultUri), 
                credential,
                new SecretClientOptions
                {
                    Retry =
                    {
                        MaxRetries = 3,
                        Delay = TimeSpan.FromSeconds(1),
                        MaxDelay = TimeSpan.FromSeconds(10),
                        Mode = RetryMode.Exponential
                    }
                });

            _isAvailable = true;
            _logger.LogInformation("Azure Key Vault provider initialized for {VaultUri}", options.VaultUri);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize Azure Key Vault provider");
            _isAvailable = false;
        }
    }

    public string ProviderName => "AzureKeyVault";

    public bool IsAvailable => _isAvailable;

    public async Task<string?> GetSecretAsync(string secretName, CancellationToken cancellationToken = default)
    {
        if (!_isAvailable || _client == null)
        {
            _logger.LogWarning("Key Vault is not available. Cannot retrieve secret {SecretName}", secretName);
            return null;
        }

        try
        {
            // Apply prefix if configured
            var fullSecretName = string.IsNullOrEmpty(_options.SecretPrefix)
                ? secretName
                : $"{_options.SecretPrefix}{secretName}";

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(TimeSpan.FromSeconds(_options.TimeoutSeconds));

            var response = await _client.GetSecretAsync(fullSecretName, cancellationToken: cts.Token);
            
            _logger.LogDebug("Successfully retrieved secret {SecretName} from Key Vault", secretName);
            return response.Value.Value;
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            _logger.LogWarning("Secret {SecretName} not found in Key Vault", secretName);
            return null;
        }
        catch (OperationCanceledException)
        {
            _logger.LogError("Timeout retrieving secret {SecretName} from Key Vault", secretName);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving secret {SecretName} from Key Vault", secretName);
            
            if (_options.ThrowOnUnavailable)
            {
                throw;
            }
            
            return null;
        }
    }

    public async Task<IDictionary<string, string?>> GetSecretsAsync(
        IEnumerable<string> secretNames, 
        CancellationToken cancellationToken = default)
    {
        var results = new Dictionary<string, string?>();
        var tasks = new List<(string Name, Task<string?> Task)>();

        foreach (var name in secretNames)
        {
            tasks.Add((name, GetSecretAsync(name, cancellationToken)));
        }

        foreach (var (name, task) in tasks)
        {
            try
            {
                results[name] = await task;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to retrieve secret {SecretName}", name);
                results[name] = null;
            }
        }

        return results;
    }
}
