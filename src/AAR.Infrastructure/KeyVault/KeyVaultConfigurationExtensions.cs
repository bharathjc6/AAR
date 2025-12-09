// =============================================================================
// AAR.Infrastructure - KeyVault/KeyVaultConfigurationExtensions.cs
// Extension methods for adding Key Vault as a configuration source
// =============================================================================

using AAR.Shared.KeyVault;
using Azure.Extensions.AspNetCore.Configuration.Secrets;
using Azure.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace AAR.Infrastructure.KeyVault;

/// <summary>
/// Extension methods for configuring Azure Key Vault integration
/// </summary>
public static class KeyVaultConfigurationExtensions
{
    /// <summary>
    /// Adds Azure Key Vault as a configuration source when UseKeyVault is enabled.
    /// This should be called early in the configuration pipeline so that secrets
    /// are available for Options binding.
    /// </summary>
    public static IHostApplicationBuilder AddKeyVaultConfiguration(this IHostApplicationBuilder builder)
    {
        var options = builder.Configuration
            .GetSection(KeyVaultOptions.SectionName)
            .Get<KeyVaultOptions>() ?? new KeyVaultOptions();

        // Check environment variable override
        var vaultUri = Environment.GetEnvironmentVariable("KEYVAULT_URI");
        if (!string.IsNullOrEmpty(vaultUri))
        {
            options.VaultUri = vaultUri;
            options.UseKeyVault = true;
        }

        // Skip if Key Vault is not enabled
        if (!options.UseKeyVault)
        {
            Console.WriteLine("[KeyVault] Key Vault integration disabled. Using local configuration.");
            return builder;
        }

        // Skip if using mock provider
        if (options.UseMockKeyVault)
        {
            Console.WriteLine("[KeyVault] Using mock Key Vault provider for local development.");
            return builder;
        }

        // Validate vault URI
        if (string.IsNullOrEmpty(options.VaultUri))
        {
            Console.WriteLine("[KeyVault] WARNING: UseKeyVault=true but VaultUri is not configured.");
            return builder;
        }

        Console.WriteLine($"[KeyVault] Connecting to Azure Key Vault: {options.VaultUri}");

        try
        {
            // Add Key Vault as a configuration source
            // DefaultAzureCredential handles Managed Identity in Azure and local dev credentials
            builder.Configuration.AddAzureKeyVault(
                new Uri(options.VaultUri),
                new DefaultAzureCredential(new DefaultAzureCredentialOptions
                {
                    ExcludeInteractiveBrowserCredential = true,
                    Retry =
                    {
                        MaxRetries = 3,
                        Delay = TimeSpan.FromSeconds(1),
                        MaxDelay = TimeSpan.FromSeconds(10)
                    }
                }),
                new AzureKeyVaultConfigurationOptions
                {
                    // Reload secrets periodically if configured
                    ReloadInterval = options.ReloadOnChange 
                        ? TimeSpan.FromSeconds(options.ReloadIntervalSeconds) 
                        : null,
                    
                    // Custom key name manager to handle prefixes and transformations
                    Manager = new KeyVaultSecretManager(options.SecretPrefix)
                });

            Console.WriteLine("[KeyVault] Azure Key Vault configuration source added successfully.");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[KeyVault] ERROR: Failed to configure Key Vault: {ex.Message}");
            
            if (options.ThrowOnUnavailable)
            {
                throw new InvalidOperationException(
                    $"Failed to connect to Azure Key Vault at {options.VaultUri}. " +
                    "Ensure the vault exists and the application has access.", ex);
            }
        }

        return builder;
    }

    /// <summary>
    /// Adds Key Vault secret provider services to the DI container
    /// </summary>
    public static IServiceCollection AddKeyVaultSecretProvider(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var options = configuration
            .GetSection(KeyVaultOptions.SectionName)
            .Get<KeyVaultOptions>() ?? new KeyVaultOptions();

        // Check environment variable override
        var vaultUri = Environment.GetEnvironmentVariable("KEYVAULT_URI");
        if (!string.IsNullOrEmpty(vaultUri))
        {
            options.VaultUri = vaultUri;
            options.UseKeyVault = true;
        }

        services.AddSingleton(options);

        if (options.UseMockKeyVault || !options.UseKeyVault)
        {
            // Use mock provider for local development and testing
            services.AddSingleton<IKeyVaultSecretProvider, MockKeyVaultSecretProvider>();
        }
        else if (!string.IsNullOrEmpty(options.VaultUri))
        {
            // Use real Azure Key Vault provider
            services.AddSingleton<IKeyVaultSecretProvider, AzureKeyVaultSecretProvider>();
        }
        else
        {
            // Fallback to mock if vault URI is not configured
            services.AddSingleton<IKeyVaultSecretProvider, MockKeyVaultSecretProvider>();
        }

        return services;
    }
}

/// <summary>
/// Custom secret manager for handling key name transformations
/// </summary>
internal class KeyVaultSecretManager : Azure.Extensions.AspNetCore.Configuration.Secrets.KeyVaultSecretManager
{
    private readonly string? _prefix;

    public KeyVaultSecretManager(string? prefix = null)
    {
        _prefix = prefix;
    }

    public override bool Load(Azure.Security.KeyVault.Secrets.SecretProperties secret)
    {
        // Load all secrets, or filter by prefix if configured
        if (string.IsNullOrEmpty(_prefix))
        {
            return true;
        }

        return secret.Name.StartsWith(_prefix, StringComparison.OrdinalIgnoreCase);
    }

    public override string GetKey(Azure.Security.KeyVault.Secrets.KeyVaultSecret secret)
    {
        var name = secret.Name;

        // Remove prefix if present
        if (!string.IsNullOrEmpty(_prefix) && name.StartsWith(_prefix, StringComparison.OrdinalIgnoreCase))
        {
            name = name.Substring(_prefix.Length);
        }

        // Convert Key Vault format (--) to configuration format (:)
        // Azure Key Vault doesn't allow colons in secret names
        return name.Replace("--", ":");
    }
}
