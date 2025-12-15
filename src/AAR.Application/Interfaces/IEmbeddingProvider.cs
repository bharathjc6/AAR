// =============================================================================
// AAR.Application - Interfaces/IEmbeddingProvider.cs
// Provider abstraction for embedding generation (BGE/Azure OpenAI)
// =============================================================================

namespace AAR.Application.Interfaces;

/// <summary>
/// Provider abstraction for embedding generation
/// </summary>
public interface IEmbeddingProvider
{
    /// <summary>
    /// Generates an embedding for a single text
    /// </summary>
    /// <param name="input">Text to embed</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Normalized embedding vector</returns>
    Task<float[]> GenerateAsync(
        string input,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Generates embeddings for multiple texts in batch
    /// </summary>
    /// <param name="inputs">Texts to embed</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>List of normalized embedding vectors</returns>
    Task<IReadOnlyList<float[]>> GenerateBatchAsync(
        IReadOnlyList<string> inputs,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the embedding dimension
    /// </summary>
    int Dimension { get; }

    /// <summary>
    /// Gets the model name
    /// </summary>
    string ModelName { get; }

    /// <summary>
    /// Gets the provider name
    /// </summary>
    string ProviderName { get; }
}
