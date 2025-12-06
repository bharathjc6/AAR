// =============================================================================
// AAR.Api - Security/JwtConfiguration.cs
// JWT Bearer authentication configuration
// =============================================================================

using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;

namespace AAR.Api.Security;

/// <summary>
/// JWT authentication configuration options
/// </summary>
public class JwtOptions
{
    public const string SectionName = "Jwt";

    /// <summary>
    /// Secret key for signing tokens (use Key Vault in production!)
    /// </summary>
    public string SecretKey { get; set; } = string.Empty;

    /// <summary>
    /// Token issuer
    /// </summary>
    public string Issuer { get; set; } = "AAR-API";

    /// <summary>
    /// Token audience
    /// </summary>
    public string Audience { get; set; } = "AAR-Clients";

    /// <summary>
    /// Token expiration in minutes
    /// </summary>
    public int ExpirationMinutes { get; set; } = 60;

    /// <summary>
    /// Refresh token expiration in days
    /// </summary>
    public int RefreshExpirationDays { get; set; } = 7;
}

/// <summary>
/// Azure AD / OpenID Connect configuration options
/// TODO: Enable this for enterprise SSO integration
/// </summary>
public class AzureAdOptions
{
    public const string SectionName = "AzureAd";

    /// <summary>
    /// Azure AD tenant ID
    /// </summary>
    public string TenantId { get; set; } = string.Empty;

    /// <summary>
    /// Azure AD application (client) ID
    /// </summary>
    public string ClientId { get; set; } = string.Empty;

    /// <summary>
    /// Azure AD instance (default: https://login.microsoftonline.com/)
    /// </summary>
    public string Instance { get; set; } = "https://login.microsoftonline.com/";

    /// <summary>
    /// Required audience for API calls
    /// </summary>
    public string Audience { get; set; } = string.Empty;

    /// <summary>
    /// Whether Azure AD authentication is enabled
    /// </summary>
    public bool Enabled { get; set; } = false;
}

/// <summary>
/// Authorization policies and roles
/// </summary>
public static class AuthPolicies
{
    // Role names
    public const string AdminRole = "Admin";
    public const string UserRole = "User";
    public const string SystemRole = "System";

    // Policy names
    public const string RequireAdmin = "RequireAdmin";
    public const string RequireUser = "RequireUser";
    public const string RequireSystem = "RequireSystem";
    public const string InternalSystem = "InternalSystem";
    public const string EnterpriseCustomer = "EnterpriseCustomer";
}

/// <summary>
/// Extension methods for configuring authentication and authorization
/// </summary>
public static class AuthenticationExtensions
{
    /// <summary>
    /// Adds JWT Bearer authentication with role-based policies
    /// </summary>
    public static IServiceCollection AddSecureAuthentication(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var jwtOptions = configuration.GetSection(JwtOptions.SectionName).Get<JwtOptions>() 
            ?? new JwtOptions();
        var azureAdOptions = configuration.GetSection(AzureAdOptions.SectionName).Get<AzureAdOptions>();

        // Get secret key from environment or configuration
        var secretKey = Environment.GetEnvironmentVariable("JWT_SECRET_KEY") 
            ?? jwtOptions.SecretKey;

        // TODO: REPLACE_WITH_KEY_VAULT - Retrieve secret from Azure Key Vault in production
        // var keyVaultUri = Environment.GetEnvironmentVariable("KEYVAULT_URI");
        // if (!string.IsNullOrEmpty(keyVaultUri))
        // {
        //     var client = new SecretClient(new Uri(keyVaultUri), new DefaultAzureCredential());
        //     secretKey = client.GetSecret("JwtSecretKey").Value.Value;
        // }

        if (string.IsNullOrEmpty(secretKey) || secretKey.Length < 32)
        {
            // Generate a random key for development (DO NOT use in production!)
            secretKey = Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));
        }

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secretKey));

        services.AddAuthentication(options =>
        {
            options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
            options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
        })
        .AddJwtBearer(options =>
        {
            options.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidIssuer = jwtOptions.Issuer,
                ValidateAudience = true,
                ValidAudience = jwtOptions.Audience,
                ValidateLifetime = true,
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = key,
                ClockSkew = TimeSpan.FromMinutes(1),
                RequireExpirationTime = true,
                RequireSignedTokens = true
            };

            options.Events = new JwtBearerEvents
            {
                OnAuthenticationFailed = context =>
                {
                    var logger = context.HttpContext.RequestServices
                        .GetRequiredService<ILogger<JwtBearerHandler>>();
                    
                    logger.LogWarning("JWT authentication failed: {Error}", 
                        context.Exception.Message);
                    
                    return Task.CompletedTask;
                },
                OnTokenValidated = context =>
                {
                    var logger = context.HttpContext.RequestServices
                        .GetRequiredService<ILogger<JwtBearerHandler>>();
                    
                    var userId = context.Principal?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                    logger.LogDebug("JWT token validated for user: {UserId}", userId);
                    
                    return Task.CompletedTask;
                }
            };
        });

        // TODO: AZURE_AD_SSO - Enable Azure AD authentication for enterprise customers
        // if (azureAdOptions?.Enabled == true)
        // {
        //     services.AddAuthentication()
        //         .AddMicrosoftIdentityWebApi(configuration, AzureAdOptions.SectionName)
        //         .EnableTokenAcquisitionToCallDownstreamApi()
        //         .AddInMemoryTokenCaches();
        // }

        // Configure authorization policies
        services.AddAuthorizationBuilder()
            .AddPolicy(AuthPolicies.RequireAdmin, policy =>
                policy.RequireRole(AuthPolicies.AdminRole))
            .AddPolicy(AuthPolicies.RequireUser, policy =>
                policy.RequireRole(AuthPolicies.UserRole, AuthPolicies.AdminRole))
            .AddPolicy(AuthPolicies.RequireSystem, policy =>
                policy.RequireRole(AuthPolicies.SystemRole))
            .AddPolicy(AuthPolicies.InternalSystem, policy =>
                policy.RequireAssertion(context =>
                {
                    // Allow API key authentication for internal system calls
                    var httpContext = context.Resource as HttpContext;
                    return httpContext?.Items.ContainsKey("ApiKeyId") == true &&
                           httpContext.Items.TryGetValue("ApiKeyScopes", out var scopes) &&
                           scopes is string[] scopeArray &&
                           scopeArray.Contains("system", StringComparer.OrdinalIgnoreCase);
                }))
            .AddPolicy(AuthPolicies.EnterpriseCustomer, policy =>
                policy.RequireClaim("customer_tier", "enterprise", "premium"));

        return services;
    }
}
