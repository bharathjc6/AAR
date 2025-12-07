// =============================================================================
// AAR.Application - Interfaces/IEmbeddingService.cs
// Interface for text embedding operations
// =============================================================================

namespace AAR.Application.Interfaces;

/// <summary>
/// Interface for generating text embeddings.
/// </summary>
public interface IEmbeddingService
{
    /// <summary>
    /// Generates an embedding for a single text.
    /// </summary>
    /// <param name="text">Text to embed</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Embedding vector</returns>
    Task<float[]> CreateEmbeddingAsync(
        string text, 
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Generates embeddings for multiple texts in batch.
    /// </summary>
    /// <param name="texts">Texts to embed</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>List of embedding vectors in same order as input</returns>
    Task<IReadOnlyList<float[]>> CreateEmbeddingsAsync(
        IEnumerable<string> texts, 
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the model name being used.
    /// </summary>
    string ModelName { get; }

    /// <summary>
    /// Gets the embedding dimension.
    /// </summary>
    int Dimension { get; }

    /// <summary>
    /// Gets whether this is a mock implementation.
    /// </summary>
    bool IsMock { get; }
}

/// <summary>
/// Embedding service configuration options
/// </summary>
public class EmbeddingOptions
{
    /// <summary>
    /// Configuration section name
    /// </summary>
    public const string SectionName = "Embedding";

    /// <summary>
    /// Azure OpenAI endpoint URL
    /// </summary>
    public string? Endpoint { get; set; }

    /// <summary>
    /// Azure OpenAI API key
    /// </summary>
    public string? ApiKey { get; set; }

    /// <summary>
    /// Embedding model deployment name
    /// </summary>
    public string Model { get; set; } = "text-embedding-ada-002";

    /// <summary>
    /// Embedding dimension (1536 for ada-002, 3072 for ada-003-large)
    /// </summary>
    public int Dimension { get; set; } = 1536;

    /// <summary>
    /// Batch size for embedding requests
    /// </summary>
    public int BatchSize { get; set; } = 16;

    /// <summary>
    /// Use mock embedding service
    /// </summary>
    public bool UseMock { get; set; } = false;

    /// <summary>
    /// Maximum retries for failed requests
    /// </summary>
    public int MaxRetries { get; set; } = 3;
}
