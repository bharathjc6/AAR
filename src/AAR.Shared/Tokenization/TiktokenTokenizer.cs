// =============================================================================
// AAR.Shared - Tokenization/TiktokenTokenizer.cs
// Tiktoken-based tokenizer for accurate token counting
// =============================================================================

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Tiktoken;

namespace AAR.Shared.Tokenization;

/// <summary>
/// Tiktoken-based tokenizer for accurate GPT-style token counting.
/// Uses the tiktoken library which implements OpenAI's tokenization algorithm.
/// </summary>
public sealed class TiktokenTokenizer : ITokenizer, IDisposable
{
    private readonly Encoder _encoder;
    private readonly ILogger<TiktokenTokenizer>? _logger;
    private readonly string _encodingName;
    private bool _disposed;

    /// <inheritdoc/>
    public string EncodingName => _encodingName;

    /// <inheritdoc/>
    public bool IsHeuristic => false;

    /// <summary>
    /// Creates a new tiktoken tokenizer with the specified encoding.
    /// </summary>
    /// <param name="options">Tokenizer options</param>
    /// <param name="logger">Optional logger</param>
    public TiktokenTokenizer(
        IOptions<TokenizerOptions> options,
        ILogger<TiktokenTokenizer>? logger = null)
    {
        _logger = logger;
        _encodingName = options.Value.Encoding;

        try
        {
            _encoder = ModelToEncoder.For(_encodingName);
            _logger?.LogInformation("Tiktoken tokenizer initialized with encoding: {Encoding}", _encodingName);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Failed to load encoding {Encoding}, falling back to gpt-4o", _encodingName);
            _encodingName = "gpt-4o";
            _encoder = ModelToEncoder.For(_encodingName);
        }
    }

    /// <summary>
    /// Creates a tiktoken tokenizer with a specific encoding name.
    /// </summary>
    /// <param name="encodingName">Name of the model (e.g., "gpt-4o")</param>
    public TiktokenTokenizer(string encodingName = "gpt-4o")
    {
        _encodingName = encodingName;
        _encoder = ModelToEncoder.For(encodingName);
    }

    /// <inheritdoc/>
    public int CountTokens(string text)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (string.IsNullOrEmpty(text))
            return 0;

        try
        {
            return _encoder.CountTokens(text);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Error counting tokens, using length-based fallback");
            // Fallback to rough estimate
            return (int)Math.Ceiling(text.Length / 4.0);
        }
    }

    /// <inheritdoc/>
    public int[] Encode(string text)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (string.IsNullOrEmpty(text))
            return [];

        try
        {
            var tokens = _encoder.Encode(text);
            return [.. tokens];
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Error encoding text");
            return [];
        }
    }

    /// <inheritdoc/>
    public string Decode(int[] tokens)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (tokens is null || tokens.Length == 0)
            return string.Empty;

        try
        {
            return _encoder.Decode([.. tokens]);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Error decoding tokens");
            return string.Empty;
        }
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _disposed = true;
        }
    }
}
