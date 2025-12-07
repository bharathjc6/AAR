// =============================================================================
// AAR.Shared - Tokenization/TokenizerOptions.cs
// Configuration options for tokenizer
// =============================================================================

namespace AAR.Shared.Tokenization;

/// <summary>
/// Tokenizer configuration options
/// </summary>
public class TokenizerOptions
{
    /// <summary>
    /// Configuration section name
    /// </summary>
    public const string SectionName = "Tokenizer";

    /// <summary>
    /// Tokenizer mode: "tiktoken", "heuristic", or "service"
    /// </summary>
    public TokenizerMode Mode { get; set; } = TokenizerMode.Tiktoken;

    /// <summary>
    /// Model name or encoding name for tiktoken (default: gpt-4o)
    /// Use model names like "gpt-4o", "gpt-4", "gpt-3.5-turbo"
    /// </summary>
    public string Encoding { get; set; } = "gpt-4o";

    /// <summary>
    /// URL for external tokenizer service (when Mode = Service)
    /// </summary>
    public string? ServiceUrl { get; set; }

    /// <summary>
    /// Timeout for service calls in milliseconds
    /// </summary>
    public int ServiceTimeoutMs { get; set; } = 5000;

    /// <summary>
    /// Whether to fallback to heuristic if tiktoken fails
    /// </summary>
    public bool FallbackToHeuristic { get; set; } = true;
}

/// <summary>
/// Tokenizer mode enumeration
/// </summary>
public enum TokenizerMode
{
    /// <summary>
    /// Use tiktoken library (recommended for accuracy)
    /// </summary>
    Tiktoken,

    /// <summary>
    /// Use heuristic estimation (fast, less accurate)
    /// </summary>
    Heuristic,

    /// <summary>
    /// Use external tokenizer service
    /// </summary>
    Service
}
