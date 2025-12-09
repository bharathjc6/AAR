// =============================================================================
// AAR.Shared - KeyVault/MockKeyVaultSecretProvider.cs
// Mock implementation for local development and testing
// =============================================================================

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace AAR.Shared.KeyVault;

/// <summary>
/// Mock Key Vault provider that reads secrets from local configuration sources:
/// 1. dotnet user-secrets
/// 2. secrets.local.json file
/// 3. Environment variables
/// </summary>
public class MockKeyVaultSecretProvider : IKeyVaultSecretProvider
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<MockKeyVaultSecretProvider> _logger;
    private readonly KeyVaultOptions _options;
    private readonly Dictionary<string, string?> _localSecrets = new();
    private bool _initialized;

    public MockKeyVaultSecretProvider(
        IConfiguration configuration,
        ILogger<MockKeyVaultSecretProvider> logger,
        KeyVaultOptions? options = null)
    {
        _configuration = configuration;
        _logger = logger;
        _options = options ?? new KeyVaultOptions();
    }

    public string ProviderName => "MockKeyVault";

    public bool IsAvailable => true; // Mock is always available

    public async Task<string?> GetSecretAsync(string secretName, CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync();

        // Convert Key Vault format (--) to configuration format (:)
        var configKey = secretName.Replace("--", ":");

        // Check local secrets cache first
        if (_localSecrets.TryGetValue(secretName, out var cachedValue))
        {
            _logger.LogDebug("Retrieved mock secret {SecretName} from local cache", secretName);
            return cachedValue;
        }

        // Try configuration (includes user-secrets, environment variables)
        var value = _configuration[configKey];
        if (!string.IsNullOrEmpty(value))
        {
            _logger.LogDebug("Retrieved mock secret {SecretName} from configuration", secretName);
            _localSecrets[secretName] = value;
            return value;
        }

        // Try environment variable with underscores
        var envKey = secretName.Replace("--", "__").Replace(":", "__");
        value = Environment.GetEnvironmentVariable(envKey);
        if (!string.IsNullOrEmpty(value))
        {
            _logger.LogDebug("Retrieved mock secret {SecretName} from environment variable {EnvKey}", secretName, envKey);
            _localSecrets[secretName] = value;
            return value;
        }

        _logger.LogWarning("Mock secret {SecretName} not found in any source", secretName);
        return null;
    }

    public async Task<IDictionary<string, string?>> GetSecretsAsync(
        IEnumerable<string> secretNames, 
        CancellationToken cancellationToken = default)
    {
        var results = new Dictionary<string, string?>();
        
        foreach (var name in secretNames)
        {
            results[name] = await GetSecretAsync(name, cancellationToken);
        }

        return results;
    }

    private async Task EnsureInitializedAsync()
    {
        if (_initialized) return;

        // Try to load from secrets.local.json
        var secretsPath = _options.LocalSecretsPath;
        if (File.Exists(secretsPath))
        {
            try
            {
                var json = await File.ReadAllTextAsync(secretsPath);
                var secrets = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
                if (secrets != null)
                {
                    foreach (var kvp in secrets)
                    {
                        _localSecrets[kvp.Key] = kvp.Value;
                    }
                    _logger.LogInformation("Loaded {Count} secrets from {Path}", secrets.Count, secretsPath);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to load secrets from {Path}", secretsPath);
            }
        }
        else
        {
            _logger.LogDebug("Local secrets file not found at {Path}, using configuration/environment", secretsPath);
        }

        _initialized = true;
    }
}
