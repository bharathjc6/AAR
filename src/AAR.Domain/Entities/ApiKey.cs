// =============================================================================
// AAR.Domain - Entities/ApiKey.cs
// Represents an API key for authentication
// =============================================================================

namespace AAR.Domain.Entities;

/// <summary>
/// Represents an API key for authenticating requests
/// </summary>
public class ApiKey : BaseEntity
{
    /// <summary>
    /// The API key value (hashed for storage)
    /// </summary>
    public string KeyHash { get; private set; } = string.Empty;
    
    /// <summary>
    /// Key prefix for identification (first 8 chars)
    /// </summary>
    public string KeyPrefix { get; private set; } = string.Empty;
    
    /// <summary>
    /// Name/description of the key
    /// </summary>
    public string Name { get; private set; } = string.Empty;
    
    /// <summary>
    /// Whether the key is active
    /// </summary>
    public bool IsActive { get; private set; } = true;
    
    /// <summary>
    /// When the key expires (null = never)
    /// </summary>
    public DateTime? ExpiresAt { get; private set; }
    
    /// <summary>
    /// Last time the key was used
    /// </summary>
    public DateTime? LastUsedAt { get; private set; }
    
    /// <summary>
    /// Number of requests made with this key
    /// </summary>
    public long RequestCount { get; private set; }
    
    /// <summary>
    /// Allowed scopes/permissions (comma-separated)
    /// </summary>
    public string? Scopes { get; private set; }

    // Private constructor for EF Core
    private ApiKey() { }

    /// <summary>
    /// Creates a new API key
    /// </summary>
    public static (ApiKey apiKey, string plainTextKey) Create(string name, DateTime? expiresAt = null, string? scopes = null)
    {
        var plainTextKey = GenerateKey();
        var keyHash = HashKey(plainTextKey);
        
        var apiKey = new ApiKey
        {
            Name = name,
            KeyHash = keyHash,
            KeyPrefix = plainTextKey[..8],
            ExpiresAt = expiresAt,
            Scopes = scopes
        };
        
        return (apiKey, plainTextKey);
    }

    /// <summary>
    /// Creates an API key from a known plain text key (for seeding/testing)
    /// </summary>
    public static ApiKey CreateFromPlainText(string plainTextKey, string name, DateTime? expiresAt = null, string scopes = "read,write")
    {
        if (string.IsNullOrWhiteSpace(plainTextKey) || !plainTextKey.StartsWith("aar_") || plainTextKey.Length < 12)
        {
            throw new ArgumentException("Invalid API key format. Must start with 'aar_' and be at least 12 characters.", nameof(plainTextKey));
        }

        var keyHash = HashKey(plainTextKey);
        
        return new ApiKey
        {
            Name = name,
            KeyHash = keyHash,
            KeyPrefix = plainTextKey[..8],
            ExpiresAt = expiresAt,
            Scopes = scopes
        };
    }

    /// <summary>
    /// Validates a plain text key against this API key
    /// </summary>
    public bool ValidateKey(string plainTextKey)
    {
        if (!IsActive) return false;
        if (ExpiresAt.HasValue && ExpiresAt.Value < DateTime.UtcNow) return false;
        
        return HashKey(plainTextKey) == KeyHash;
    }

    /// <summary>
    /// Records usage of this key
    /// </summary>
    public void RecordUsage()
    {
        LastUsedAt = DateTime.UtcNow;
        RequestCount++;
    }

    /// <summary>
    /// Deactivates this key
    /// </summary>
    public void Deactivate()
    {
        IsActive = false;
        SetUpdated();
    }

    /// <summary>
    /// Generates a new random API key
    /// </summary>
    private static string GenerateKey()
    {
        // Format: aar_xxxxxxxxxxxxxxxxxxxxxxxxxxxx (total 36 chars)
        var bytes = new byte[32]; // Use more bytes to ensure enough characters after cleanup
        using var rng = System.Security.Cryptography.RandomNumberGenerator.Create();
        rng.GetBytes(bytes);
        var base64 = Convert.ToBase64String(bytes)
            .Replace("+", "")
            .Replace("/", "")
            .Replace("=", "");
        // Take first 32 alphanumeric chars (base64 without special chars gives ~42 chars from 32 bytes)
        return $"aar_{base64[..32]}";
    }

    /// <summary>
    /// Hashes an API key for storage
    /// </summary>
    private static string HashKey(string key)
    {
        using var sha256 = System.Security.Cryptography.SHA256.Create();
        var bytes = System.Text.Encoding.UTF8.GetBytes(key);
        var hash = sha256.ComputeHash(bytes);
        return Convert.ToBase64String(hash);
    }
}
