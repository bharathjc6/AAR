// =============================================================================
// AAR.Shared - Tokenization/ITokenizer.cs
// Abstraction for token counting and encoding
// =============================================================================

namespace AAR.Shared.Tokenization;

/// <summary>
/// Interface for tokenization operations.
/// Used for accurate token counting before sending to LLMs.
/// </summary>
public interface ITokenizer
{
    /// <summary>
    /// Counts the number of tokens in the given text.
    /// </summary>
    /// <param name="text">The text to tokenize</param>
    /// <returns>Number of tokens</returns>
    int CountTokens(string text);

    /// <summary>
    /// Encodes text into token IDs.
    /// </summary>
    /// <param name="text">The text to encode</param>
    /// <returns>Array of token IDs</returns>
    int[] Encode(string text);

    /// <summary>
    /// Decodes token IDs back into text.
    /// </summary>
    /// <param name="tokens">Array of token IDs</param>
    /// <returns>Decoded text</returns>
    string Decode(int[] tokens);

    /// <summary>
    /// Gets the name of the encoding/model being used.
    /// </summary>
    string EncodingName { get; }

    /// <summary>
    /// Gets whether this is a fallback/heuristic implementation.
    /// </summary>
    bool IsHeuristic { get; }
}
