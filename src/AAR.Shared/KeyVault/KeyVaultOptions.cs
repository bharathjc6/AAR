// =============================================================================
// AAR.Shared - KeyVault/KeyVaultOptions.cs
// Configuration options for Azure Key Vault integration
// =============================================================================

namespace AAR.Shared.KeyVault;

/// <summary>
/// Configuration options for Key Vault integration
/// </summary>
public class KeyVaultOptions
{
    /// <summary>
    /// Configuration section name
    /// </summary>
    public const string SectionName = "KeyVault";

    /// <summary>
    /// The URI of the Azure Key Vault (e.g., https://my-vault.vault.azure.net/)
    /// </summary>
    public string? VaultUri { get; set; }

    /// <summary>
    /// Whether to use Key Vault for secrets. Default is false for local development.
    /// </summary>
    public bool UseKeyVault { get; set; } = false;

    /// <summary>
    /// Whether to use mock Key Vault provider for local development/testing.
    /// When true, secrets are loaded from user-secrets or secrets.local.json.
    /// </summary>
    public bool UseMockKeyVault { get; set; } = false;

    /// <summary>
    /// Path to local secrets file when using mock provider.
    /// Default: secrets.local.json in the application directory.
    /// </summary>
    public string LocalSecretsPath { get; set; } = "secrets.local.json";

    /// <summary>
    /// Prefix for secrets in Key Vault. If set, all secret names will be prefixed.
    /// Example: "AAR-" would look for "AAR-ConnectionStrings--DefaultConnection"
    /// </summary>
    public string? SecretPrefix { get; set; }

    /// <summary>
    /// Timeout for Key Vault operations in seconds. Default: 30
    /// </summary>
    public int TimeoutSeconds { get; set; } = 30;

    /// <summary>
    /// Whether to throw an exception if Key Vault is unavailable.
    /// If false, the application will continue with empty secrets.
    /// </summary>
    public bool ThrowOnUnavailable { get; set; } = true;

    /// <summary>
    /// List of secret names to preload at startup.
    /// These secrets will be loaded into IConfiguration immediately.
    /// </summary>
    public List<string> PreloadSecrets { get; set; } = new();

    /// <summary>
    /// Whether to reload secrets when they change in Key Vault.
    /// Requires Azure Key Vault change notification.
    /// </summary>
    public bool ReloadOnChange { get; set; } = false;

    /// <summary>
    /// Reload interval in seconds when ReloadOnChange is enabled.
    /// </summary>
    public int ReloadIntervalSeconds { get; set; } = 300;
}
