// =============================================================================
// AAR.Infrastructure - Services/Validation/SchemaValidator.cs
// JSON schema validation for agent outputs
// =============================================================================

using System.Text.Json;
using AAR.Application.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AAR.Infrastructure.Services.Validation;

/// <summary>
/// JSON schema validator for agent outputs.
/// Uses simple validation rules rather than full JSON Schema library.
/// </summary>
public class SchemaValidator : ISchemaValidator
{
    private readonly SchemaValidationOptions _options;
    private readonly ILogger<SchemaValidator> _logger;

    public SchemaValidator(
        IOptions<SchemaValidationOptions> options,
        ILogger<SchemaValidator> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    /// <inheritdoc/>
    public ValidationResult ValidateFindingSchema(string json)
    {
        return ValidateAgentResponse(json, SchemaType.Finding);
    }

    /// <inheritdoc/>
    public ValidationResult ValidateAgentResponse(string json, SchemaType schemaType)
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(json))
        {
            return ValidationResult.Failure("JSON response is empty");
        }

        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            switch (schemaType)
            {
                case SchemaType.Finding:
                    ValidateFinding(root, errors);
                    break;

                case SchemaType.AgentResponse:
                    ValidateAgentResponseSchema(root, errors);
                    break;

                case SchemaType.Report:
                    ValidateReportSchema(root, errors);
                    break;
            }
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Failed to parse JSON for schema validation");
            return ValidationResult.Failure($"Invalid JSON: {ex.Message}");
        }

        if (errors.Count > 0)
        {
            _logger.LogWarning("Schema validation failed with {ErrorCount} errors", errors.Count);
            return ValidationResult.Failure(errors);
        }

        return ValidationResult.Success();
    }

    /// <inheritdoc/>
    public string GenerateCorrectivePrompt(string originalJson, IReadOnlyList<string> errors)
    {
        var errorList = string.Join("\n- ", errors);
        
        return $@"The previous JSON response had validation errors:
- {errorList}

Please fix the JSON to match the expected schema. Requirements:
1. All findings must have: description (string), severity (High/Medium/Low/Info), category (Architecture/Security/Performance/Maintainability/CodeQuality/Testing/Documentation/BestPractice/Other)
2. Include 'sources' array with chunkId and filePath for each finding
3. Each source should have: chunkId (string), filePath (string), startLine (int), endLine (int)
4. Return only valid JSON, no additional text or markdown

Original JSON:
{originalJson}

Please provide the corrected JSON:";
    }

    private void ValidateFinding(JsonElement element, List<string> errors, string path = "")
    {
        // Required fields
        if (!element.TryGetProperty("description", out var desc) || 
            desc.ValueKind != JsonValueKind.String ||
            string.IsNullOrWhiteSpace(desc.GetString()))
        {
            errors.Add($"{path}description is required and must be a non-empty string");
        }

        if (!element.TryGetProperty("severity", out var severity) || 
            severity.ValueKind != JsonValueKind.String)
        {
            errors.Add($"{path}severity is required and must be a string");
        }
        else
        {
            var severityValue = severity.GetString();
            var validSeverities = new[] { "High", "Medium", "Low", "Info" };
            if (!validSeverities.Contains(severityValue, StringComparer.OrdinalIgnoreCase))
            {
                errors.Add($"{path}severity must be one of: {string.Join(", ", validSeverities)}");
            }
        }

        if (!element.TryGetProperty("category", out var category) || 
            category.ValueKind != JsonValueKind.String)
        {
            errors.Add($"{path}category is required and must be a string");
        }
        else
        {
            var categoryValue = category.GetString();
            var validCategories = new[] 
            { 
                "Architecture", "Security", "Performance", "Maintainability", 
                "CodeQuality", "Testing", "Documentation", "Accessibility",
                "BestPractice", "Other" 
            };
            if (!validCategories.Contains(categoryValue, StringComparer.OrdinalIgnoreCase))
            {
                errors.Add($"{path}category must be one of: {string.Join(", ", validCategories)}");
            }
        }

        // Validate sources if present
        if (element.TryGetProperty("sources", out var sources))
        {
            if (sources.ValueKind != JsonValueKind.Array)
            {
                errors.Add($"{path}sources must be an array");
            }
            else
            {
                var index = 0;
                foreach (var source in sources.EnumerateArray())
                {
                    ValidateSource(source, errors, $"{path}sources[{index}].");
                    index++;
                }
            }
        }
    }

    private void ValidateSource(JsonElement element, List<string> errors, string path)
    {
        if (!element.TryGetProperty("chunkId", out var chunkId) || 
            chunkId.ValueKind != JsonValueKind.String)
        {
            errors.Add($"{path}chunkId is required");
        }

        if (!element.TryGetProperty("filePath", out var filePath) || 
            filePath.ValueKind != JsonValueKind.String)
        {
            errors.Add($"{path}filePath is required");
        }
    }

    private void ValidateAgentResponseSchema(JsonElement element, List<string> errors)
    {
        // Check for findings array
        if (!element.TryGetProperty("findings", out var findings))
        {
            // Try lowercase
            if (!element.TryGetProperty("Findings", out findings))
            {
                errors.Add("AgentResponse must have a 'findings' array");
                return;
            }
        }

        if (findings.ValueKind != JsonValueKind.Array)
        {
            errors.Add("'findings' must be an array");
            return;
        }

        var index = 0;
        foreach (var finding in findings.EnumerateArray())
        {
            ValidateFinding(finding, errors, $"findings[{index}].");
            index++;
        }

        // Check for summary
        if (!element.TryGetProperty("summary", out _) && 
            !element.TryGetProperty("Summary", out _))
        {
            // Summary is recommended but not required
            _logger.LogDebug("AgentResponse missing recommended 'summary' field");
        }
    }

    private void ValidateReportSchema(JsonElement element, List<string> errors)
    {
        // Required fields for report
        var requiredFields = new[] { "projectId", "summary", "healthScore", "findings" };
        
        foreach (var field in requiredFields)
        {
            if (!element.TryGetProperty(field, out _) && 
                !element.TryGetProperty(char.ToUpperInvariant(field[0]) + field[1..], out _))
            {
                errors.Add($"Report must have '{field}' field");
            }
        }

        // Validate findings array
        if (element.TryGetProperty("findings", out var findings) || 
            element.TryGetProperty("Findings", out findings))
        {
            if (findings.ValueKind == JsonValueKind.Array)
            {
                var index = 0;
                foreach (var finding in findings.EnumerateArray())
                {
                    ValidateFinding(finding, errors, $"findings[{index}].");
                    index++;
                }
            }
        }
    }
}
