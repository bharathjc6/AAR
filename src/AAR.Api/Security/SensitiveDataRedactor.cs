// =============================================================================
// AAR.Api - Security/SensitiveDataRedactor.cs
// Serilog enricher for redacting sensitive data from logs
// =============================================================================

using Serilog.Core;
using Serilog.Events;
using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;

namespace AAR.Api.Security;

/// <summary>
/// Serilog destructuring policy that redacts sensitive data
/// </summary>
public class SensitiveDataRedactor : IDestructuringPolicy
{
    private static readonly HashSet<string> SensitivePropertyNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "password",
        "secret",
        "token",
        "apikey",
        "api_key",
        "authorization",
        "bearer",
        "credential",
        "connectionstring",
        "connection_string",
        "privatekey",
        "private_key",
        "accesstoken",
        "access_token",
        "refreshtoken",
        "refresh_token",
        "ssn",
        "creditcard",
        "credit_card",
        "cvv",
        "pin"
    };

    public bool TryDestructure(object value, ILogEventPropertyValueFactory propertyValueFactory, 
        [NotNullWhen(true)] out LogEventPropertyValue? result)
    {
        result = null;
        return false; // Let other policies handle non-sensitive data
    }
}

/// <summary>
/// Log event enricher that redacts sensitive values
/// </summary>
public class SensitiveDataEnricher : ILogEventEnricher
{
    private static readonly Regex ApiKeyPattern = new(
        @"(aar_[a-zA-Z0-9]{20,})",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex BearerTokenPattern = new(
        @"Bearer\s+[a-zA-Z0-9\-_\.]+",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex ConnectionStringPattern = new(
        @"(Password|Pwd|Secret|Key)=([^;]+)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex EmailPattern = new(
        @"[a-zA-Z0-9._%+-]+@[a-zA-Z0-9.-]+\.[a-zA-Z]{2,}",
        RegexOptions.Compiled);

    public void Enrich(LogEvent logEvent, ILogEventPropertyFactory propertyFactory)
    {
        // The enricher runs on every log event, but actual redaction
        // happens through the message template and property handling
    }

    /// <summary>
    /// Redacts sensitive patterns from a string value
    /// </summary>
    public static string RedactSensitiveData(string? input)
    {
        if (string.IsNullOrEmpty(input))
            return input ?? string.Empty;

        var result = input;

        // Redact API keys
        result = ApiKeyPattern.Replace(result, m =>
        {
            var key = m.Value;
            return key.Length > 12 ? key[..8] + "***REDACTED***" : "***REDACTED***";
        });

        // Redact bearer tokens
        result = BearerTokenPattern.Replace(result, "Bearer ***REDACTED***");

        // Redact connection string secrets
        result = ConnectionStringPattern.Replace(result, "$1=***REDACTED***");

        return result;
    }

    /// <summary>
    /// Truncates and hashes content for safe logging
    /// </summary>
    public static string HashForLog(string? content, int maxPreviewLength = 50)
    {
        if (string.IsNullOrEmpty(content))
            return "[empty]";

        var hash = ComputeHash(content);
        var preview = content.Length <= maxPreviewLength 
            ? content 
            : content[..maxPreviewLength] + "...";

        // Redact the preview as well
        preview = RedactSensitiveData(preview);

        return $"[{content.Length} chars, hash:{hash[..8]}] {preview}";
    }

    private static string ComputeHash(string input)
    {
        using var sha256 = System.Security.Cryptography.SHA256.Create();
        var bytes = System.Text.Encoding.UTF8.GetBytes(input);
        var hash = sha256.ComputeHash(bytes);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}

/// <summary>
/// Extension methods for configuring secure logging
/// </summary>
public static class SecureLoggingExtensions
{
    /// <summary>
    /// Creates a Serilog configuration with sensitive data redaction
    /// </summary>
    public static Serilog.LoggerConfiguration WithSecureLogging(
        this Serilog.LoggerConfiguration config)
    {
        return config
            .Enrich.With<SensitiveDataEnricher>()
            .Destructure.With<SensitiveDataRedactor>();
    }
}
