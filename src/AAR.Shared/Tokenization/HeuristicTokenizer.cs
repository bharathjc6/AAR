// =============================================================================
// AAR.Shared - Tokenization/HeuristicTokenizer.cs
// Fallback tokenizer using heuristic estimation
// =============================================================================

using System.Text.RegularExpressions;

namespace AAR.Shared.Tokenization;

/// <summary>
/// Heuristic tokenizer that estimates token counts without external dependencies.
/// Uses character and word-based estimation tuned for GPT-style tokenizers.
/// Less accurate but reliable and fast.
/// </summary>
public sealed partial class HeuristicTokenizer : ITokenizer
{
    /// <inheritdoc/>
    public string EncodingName => "heuristic";

    /// <inheritdoc/>
    public bool IsHeuristic => true;

    /// <inheritdoc/>
    public int CountTokens(string text)
    {
        if (string.IsNullOrEmpty(text))
            return 0;

        // Heuristic: GPT tokenizers average ~4 characters per token for English text
        // Code tends to be slightly more efficient due to common patterns
        // We use a weighted approach combining character and word counts
        
        var charCount = text.Length;
        var wordCount = CountWords(text);
        var specialCharCount = CountSpecialTokenChars(text);

        // Base estimation: ~4 chars per token
        var charBasedEstimate = (int)Math.Ceiling(charCount / 4.0);
        
        // Word-based adjustment: each word is roughly 1-2 tokens
        var wordBasedEstimate = (int)Math.Ceiling(wordCount * 1.3);
        
        // Special characters often become separate tokens
        var specialTokenEstimate = specialCharCount / 2;

        // Weighted average favoring character-based for code
        var estimate = (int)Math.Ceiling(
            (charBasedEstimate * 0.6) + 
            (wordBasedEstimate * 0.3) + 
            (specialTokenEstimate * 0.1));

        return Math.Max(1, estimate);
    }

    /// <inheritdoc/>
    public int[] Encode(string text)
    {
        if (string.IsNullOrEmpty(text))
            return [];

        // Generate pseudo-tokens based on word boundaries and special characters
        var tokens = new List<int>();
        var tokenId = 0;

        // Simple tokenization: split on whitespace and punctuation
        var parts = TokenSplitRegex().Split(text)
            .Where(p => !string.IsNullOrEmpty(p));

        foreach (var part in parts)
        {
            // Assign sequential token IDs (not real token IDs, just for interface compliance)
            tokens.Add(tokenId++);
            
            // Long words get additional tokens
            if (part.Length > 8)
            {
                tokens.Add(tokenId++);
            }
        }

        return [.. tokens];
    }

    /// <inheritdoc/>
    public string Decode(int[] tokens)
    {
        // Heuristic decoder cannot accurately decode since we don't have a real vocabulary
        // Return a placeholder indicating the limitation
        return $"[Heuristic decoder: {tokens.Length} tokens]";
    }

    private static int CountWords(string text)
    {
        return WordRegex().Matches(text).Count;
    }

    private static int CountSpecialTokenChars(string text)
    {
        return SpecialCharRegex().Matches(text).Count;
    }

    [GeneratedRegex(@"\s+|(?=[{}()\[\];,.<>:=+\-*/&|!?@#$%^~`\\])")]
    private static partial Regex TokenSplitRegex();

    [GeneratedRegex(@"\b\w+\b")]
    private static partial Regex WordRegex();

    [GeneratedRegex(@"[{}()\[\];,.<>:=+\-*/&|!?@#$%^~`\\'\""]")]
    private static partial Regex SpecialCharRegex();
}
