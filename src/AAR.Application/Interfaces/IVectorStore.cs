// =============================================================================
// AAR.Application - Interfaces/IVectorStore.cs
// Interface for vector storage and retrieval
// =============================================================================

namespace AAR.Application.Interfaces;

/// <summary>
/// Interface for vector storage and similarity search.
/// </summary>
public interface IVectorStore
{
    /// <summary>
    /// Indexes a vector with associated metadata.
    /// </summary>
    /// <param name="chunkId">Unique chunk identifier</param>
    /// <param name="vector">Embedding vector</param>
    /// <param name="metadata">Associated metadata</param>
    /// <param name="cancellationToken">Cancellation token</param>
    Task IndexVectorAsync(
        string chunkId,
        float[] vector,
        VectorMetadata metadata,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Indexes multiple vectors in batch.
    /// </summary>
    Task IndexVectorsAsync(
        IEnumerable<(string chunkId, float[] vector, VectorMetadata metadata)> vectors,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Queries for similar vectors.
    /// </summary>
    /// <param name="queryVector">Query embedding vector</param>
    /// <param name="topK">Number of results to return</param>
    /// <param name="projectId">Optional project filter</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Top K similar chunks with scores</returns>
    Task<IReadOnlyList<VectorSearchResult>> QueryAsync(
        float[] queryVector,
        int topK = 10,
        Guid? projectId = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes vectors for a project.
    /// </summary>
    Task DeleteByProjectIdAsync(Guid projectId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes a specific vector.
    /// </summary>
    Task DeleteAsync(string chunkId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the count of indexed vectors for a project.
    /// </summary>
    Task<int> CountAsync(Guid? projectId = null, CancellationToken cancellationToken = default);
}

/// <summary>
/// Metadata associated with a vector.
/// </summary>
public record VectorMetadata
{
    /// <summary>
    /// Project ID
    /// </summary>
    public required Guid ProjectId { get; init; }

    /// <summary>
    /// File path
    /// </summary>
    public required string FilePath { get; init; }

    /// <summary>
    /// Start line (1-based)
    /// </summary>
    public required int StartLine { get; init; }

    /// <summary>
    /// End line (1-based, inclusive)
    /// </summary>
    public required int EndLine { get; init; }

    /// <summary>
    /// Programming language
    /// </summary>
    public string? Language { get; init; }

    /// <summary>
    /// Semantic type (class, method, etc.)
    /// </summary>
    public string? SemanticType { get; init; }

    /// <summary>
    /// Semantic name
    /// </summary>
    public string? SemanticName { get; init; }

    /// <summary>
    /// Token count
    /// </summary>
    public int TokenCount { get; init; }

    /// <summary>
    /// Text content (optional)
    /// </summary>
    public string? Content { get; init; }
    
    /// <summary>
    /// Namespace or module owning this chunk (if available)
    /// </summary>
    public string? Namespace { get; init; }

    /// <summary>
    /// Responsibility/owner inferred from namespace or folder (best-effort)
    /// </summary>
    public string? Responsibility { get; init; }

    /// <summary>
    /// Index of this chunk within its semantic unit (0-based)
    /// </summary>
    public int ChunkIndex { get; init; }

    /// <summary>
    /// Total number of chunks in the semantic unit
    /// </summary>
    public int TotalChunks { get; init; }
}

/// <summary>
/// Result from a vector similarity search.
/// </summary>
public record VectorSearchResult
{
    /// <summary>
    /// Chunk ID
    /// </summary>
    public required string ChunkId { get; init; }

    /// <summary>
    /// Similarity score (0-1, higher is more similar)
    /// </summary>
    public required float Score { get; init; }

    /// <summary>
    /// Associated metadata
    /// </summary>
    public required VectorMetadata Metadata { get; init; }
}

/// <summary>
/// Vector store configuration options
/// </summary>
public class VectorStoreOptions
{
    /// <summary>
    /// Configuration section name
    /// </summary>
    public const string SectionName = "VectorStore";

    /// <summary>
    /// Vector store type: "InMemory" or "Cosmos"
    /// </summary>
    public string Type { get; set; } = "InMemory";

    /// <summary>
    /// Cosmos DB connection string (for Cosmos type)
    /// </summary>
    public string? CosmosConnectionString { get; set; }

    /// <summary>
    /// Cosmos DB database name
    /// </summary>
    public string? CosmosDatabaseName { get; set; }

    /// <summary>
    /// Cosmos DB container name
    /// </summary>
    public string? CosmosContainerName { get; set; }
}
