// =============================================================================
// AAR.Infrastructure - Services/Embedding/MockEmbeddingService.cs
// Mock embedding service for development and testing
// =============================================================================

using AAR.Application.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AAR.Infrastructure.Services.Embedding;

/// <summary>
/// Mock embedding service that generates deterministic pseudo-vectors.
/// Useful for development and testing without Azure OpenAI costs.
/// </summary>
public class MockEmbeddingService : IEmbeddingService
{
    private readonly EmbeddingOptions _options;
    private readonly ILogger<MockEmbeddingService> _logger;

    /// <inheritdoc/>
    public string ModelName => "mock-embedding";

    /// <inheritdoc/>
    public int Dimension => _options.Dimension;

    /// <inheritdoc/>
    public bool IsMock => true;

    public MockEmbeddingService(
        IOptions<EmbeddingOptions> options,
        ILogger<MockEmbeddingService> logger)
    {
        _options = options.Value;
        _logger = logger;
        _logger.LogInformation("MockEmbeddingService initialized with dimension: {Dimension}", _options.Dimension);
    }

    /// <inheritdoc/>
    public Task<float[]> CreateEmbeddingAsync(string text, CancellationToken cancellationToken = default)
    {
        var embedding = GenerateDeterministicEmbedding(text);
        return Task.FromResult(embedding);
    }

    /// <inheritdoc/>
    public Task<IReadOnlyList<float[]>> CreateEmbeddingsAsync(
        IEnumerable<string> texts, 
        CancellationToken cancellationToken = default)
    {
        var embeddings = texts.Select(GenerateDeterministicEmbedding).ToList();
        _logger.LogDebug("Generated {Count} mock embeddings", embeddings.Count);
        return Task.FromResult<IReadOnlyList<float[]>>(embeddings);
    }

    /// <summary>
    /// Generates a deterministic pseudo-embedding based on the text content.
    /// Similar texts will have similar (but not identical) embeddings.
    /// </summary>
    private float[] GenerateDeterministicEmbedding(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return new float[_options.Dimension];

        var embedding = new float[_options.Dimension];
        var hash = GetStableHash(text);
        var random = new Random(hash);

        // Generate base random values
        for (var i = 0; i < _options.Dimension; i++)
        {
            embedding[i] = (float)(random.NextDouble() * 2 - 1); // Range [-1, 1]
        }

        // Add some text-based features for semantic similarity
        AddTextFeatures(embedding, text);

        // Normalize to unit vector (important for cosine similarity)
        Normalize(embedding);

        return embedding;
    }

    /// <summary>
    /// Adds text-based features to make similar texts have more similar embeddings.
    /// </summary>
    private void AddTextFeatures(float[] embedding, string text)
    {
        // Use word-level features to influence embedding
        var words = text.ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var wordSet = new HashSet<string>(words);

        // Common programming keywords affect specific dimensions
        var keywordInfluence = new Dictionary<string, int>
        {
            ["class"] = 0,
            ["public"] = 1,
            ["private"] = 2,
            ["void"] = 3,
            ["async"] = 4,
            ["await"] = 5,
            ["return"] = 6,
            ["if"] = 7,
            ["else"] = 8,
            ["for"] = 9,
            ["while"] = 10,
            ["try"] = 11,
            ["catch"] = 12,
            ["throw"] = 13,
            ["new"] = 14,
            ["null"] = 15,
            ["interface"] = 16,
            ["abstract"] = 17,
            ["static"] = 18,
            ["readonly"] = 19,
            ["const"] = 20,
            ["using"] = 21,
            ["namespace"] = 22,
            ["var"] = 23,
            ["string"] = 24,
            ["int"] = 25,
            ["bool"] = 26,
            ["list"] = 27,
            ["dictionary"] = 28,
            ["task"] = 29
        };

        foreach (var (keyword, dimension) in keywordInfluence)
        {
            if (wordSet.Contains(keyword) && dimension < embedding.Length)
            {
                embedding[dimension] += 0.5f;
            }
        }

        // Text length influences a dimension
        var lengthInfluence = Math.Min(text.Length / 1000f, 1f);
        if (embedding.Length > 30)
        {
            embedding[30] = lengthInfluence;
        }

        // Word count influences another dimension
        var wordCountInfluence = Math.Min(words.Length / 100f, 1f);
        if (embedding.Length > 31)
        {
            embedding[31] = wordCountInfluence;
        }
    }

    /// <summary>
    /// Normalizes the embedding to a unit vector.
    /// </summary>
    private static void Normalize(float[] embedding)
    {
        var magnitude = MathF.Sqrt(embedding.Sum(x => x * x));
        
        if (magnitude > 0)
        {
            for (var i = 0; i < embedding.Length; i++)
            {
                embedding[i] /= magnitude;
            }
        }
    }

    /// <summary>
    /// Generates a stable hash code for a string.
    /// </summary>
    private static int GetStableHash(string text)
    {
        unchecked
        {
            var hash = 17;
            foreach (var c in text)
            {
                hash = hash * 31 + c;
            }
            return hash;
        }
    }
}
