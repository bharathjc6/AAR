// =============================================================================
// AAR.Application - Interfaces/ISchemaValidator.cs
// Interface for JSON schema validation of agent outputs
// =============================================================================

namespace AAR.Application.Interfaces;

/// <summary>
/// Interface for validating agent outputs against JSON schemas.
/// </summary>
public interface ISchemaValidator
{
    /// <summary>
    /// Validates a JSON string against the finding schema.
    /// </summary>
    /// <param name="json">JSON string to validate</param>
    /// <returns>Validation result with any errors</returns>
    ValidationResult ValidateFindingSchema(string json);

    /// <summary>
    /// Validates agent response against expected schema.
    /// </summary>
    /// <param name="json">JSON response from agent</param>
    /// <param name="schemaType">Type of schema to validate against</param>
    /// <returns>Validation result</returns>
    ValidationResult ValidateAgentResponse(string json, SchemaType schemaType);

    /// <summary>
    /// Generates a corrective prompt for invalid JSON.
    /// </summary>
    /// <param name="originalJson">The invalid JSON</param>
    /// <param name="errors">Validation errors</param>
    /// <returns>Corrective prompt to fix the JSON</returns>
    string GenerateCorrectivePrompt(string originalJson, IReadOnlyList<string> errors);
}

/// <summary>
/// Result of schema validation.
/// </summary>
public record ValidationResult
{
    /// <summary>
    /// Whether the validation passed
    /// </summary>
    public bool IsValid { get; init; }

    /// <summary>
    /// List of validation errors
    /// </summary>
    public IReadOnlyList<string> Errors { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Creates a successful validation result
    /// </summary>
    public static ValidationResult Success() => new() { IsValid = true };

    /// <summary>
    /// Creates a failed validation result
    /// </summary>
    public static ValidationResult Failure(params string[] errors) => 
        new() { IsValid = false, Errors = errors };

    /// <summary>
    /// Creates a failed validation result
    /// </summary>
    public static ValidationResult Failure(IReadOnlyList<string> errors) => 
        new() { IsValid = false, Errors = errors };
}

/// <summary>
/// Types of schemas for validation
/// </summary>
public enum SchemaType
{
    /// <summary>
    /// Individual finding schema
    /// </summary>
    Finding,

    /// <summary>
    /// Agent response with multiple findings
    /// </summary>
    AgentResponse,

    /// <summary>
    /// Complete report schema
    /// </summary>
    Report
}

/// <summary>
/// Schema validation options
/// </summary>
public class SchemaValidationOptions
{
    /// <summary>
    /// Configuration section name
    /// </summary>
    public const string SectionName = "SchemaValidation";

    /// <summary>
    /// Maximum retries for corrective prompts
    /// </summary>
    public int MaxRetries { get; set; } = 3;

    /// <summary>
    /// Whether to enforce strict schema validation
    /// </summary>
    public bool StrictMode { get; set; } = true;

    /// <summary>
    /// Path to custom schemas directory
    /// </summary>
    public string? SchemasPath { get; set; }
}
