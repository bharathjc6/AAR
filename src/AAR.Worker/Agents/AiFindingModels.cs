using System.Text.Json;
using System.Text.Json.Serialization;

namespace AAR.Worker.Agents;

/// <summary>
/// Shared AI finding models with robust JSON deserialization.
/// Handles cases where LLM returns arrays instead of strings for fields like filePath.
/// </summary>
public static class AiFindingModels
{
    /// <summary>
    /// JSON serializer options configured to handle LLM response quirks
    /// </summary>
    public static JsonSerializerOptions JsonOptions { get; } = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new FlexibleStringConverter() }
    };
}

/// <summary>
/// JSON converter that handles cases where a string field may be returned as:
/// - A string (normal case)
/// - An array of strings (LLM quirk - we take the first element)
/// - A number (we convert to string)
/// - null
/// </summary>
public class FlexibleStringConverter : JsonConverter<string?>
{
    public override string? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        return reader.TokenType switch
        {
            JsonTokenType.String => reader.GetString(),
            JsonTokenType.Null => null,
            JsonTokenType.Number => reader.TryGetInt64(out var l) ? l.ToString() : reader.GetDouble().ToString(),
            JsonTokenType.True => "true",
            JsonTokenType.False => "false",
            JsonTokenType.StartArray => ReadFirstArrayElement(ref reader),
            JsonTokenType.StartObject => ReadObjectAsString(ref reader),
            _ => SkipAndReturnNull(ref reader)
        };
    }

    private static string? ReadFirstArrayElement(ref Utf8JsonReader reader)
    {
        string? firstElement = null;
        var depth = reader.CurrentDepth;

        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndArray && reader.CurrentDepth == depth)
                break;

            // Take the first string element we find
            if (firstElement == null)
            {
                firstElement = reader.TokenType switch
                {
                    JsonTokenType.String => reader.GetString(),
                    JsonTokenType.Number => reader.TryGetInt64(out var l) ? l.ToString() : reader.GetDouble().ToString(),
                    JsonTokenType.StartObject => SkipObjectAndReturnNull(ref reader),
                    JsonTokenType.StartArray => ReadFirstArrayElement(ref reader), // nested array
                    _ => null
                };
            }
            else if (reader.TokenType == JsonTokenType.StartObject || reader.TokenType == JsonTokenType.StartArray)
            {
                // Skip nested structures we don't need
                reader.Skip();
            }
        }

        return firstElement;
    }

    private static string? ReadObjectAsString(ref Utf8JsonReader reader)
    {
        // Skip the object entirely and return null - we can't convert an object to string
        reader.Skip();
        return null;
    }

    private static string? SkipObjectAndReturnNull(ref Utf8JsonReader reader)
    {
        reader.Skip();
        return null;
    }

    private static string? SkipAndReturnNull(ref Utf8JsonReader reader)
    {
        reader.Skip();
        return null;
    }

    public override void Write(Utf8JsonWriter writer, string? value, JsonSerializerOptions options)
    {
        if (value == null)
            writer.WriteNullValue();
        else
            writer.WriteStringValue(value);
    }
}

/// <summary>
/// Standard AI finding model for single-file findings
/// </summary>
public class AiFinding
{
    public string? Id { get; set; }
    public string? Title { get; set; }
    public string? Description { get; set; }
    public string? Explanation { get; set; }
    public string? Severity { get; set; }
    public string? Category { get; set; }
    public string? CweId { get; set; }
    public string? FilePath { get; set; }
    public AiLineRange? LineRange { get; set; }
    public string? Symbol { get; set; }
    public double Confidence { get; set; }
    public string? SuggestedFix { get; set; }
    public string? CodeSnippet { get; set; }
    public string? FixedCodeSnippet { get; set; }
    public string? OriginalCodeSnippet { get; set; }
}

/// <summary>
/// Line range model for AI findings
/// </summary>
public class AiLineRange
{
    public int Start { get; set; }
    public int End { get; set; }
}

/// <summary>
/// AI finding model for cluster-based analysis (multiple affected files)
/// </summary>
public class ClusterAiFinding
{
    public string? Id { get; set; }
    public string? Description { get; set; }
    public string? Explanation { get; set; }
    public string? Severity { get; set; }
    public string? Category { get; set; }
    public List<string>? AffectedFiles { get; set; }
    public double Confidence { get; set; }
    public string? SuggestedFix { get; set; }
}
