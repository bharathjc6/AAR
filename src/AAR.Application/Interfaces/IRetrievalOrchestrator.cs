// =============================================================================
// AAR.Application - Interfaces/IRetrievalOrchestrator.cs
// Interface for retrieval-augmented generation orchestration
// =============================================================================

namespace AAR.Application.Interfaces;

/// <summary>
/// Orchestrates retrieval-augmented generation with hierarchical summarization.
/// </summary>
public interface IRetrievalOrchestrator
{
    /// <summary>
    /// Retrieves relevant context for a query with automatic summarization if needed.
    /// </summary>
    /// <param name="projectId">Project ID to search</param>
    /// <param name="query">Query text</param>
    /// <param name="maxTokens">Maximum tokens for context</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Retrieved context with sources</returns>
    Task<RetrievalResult> RetrieveContextAsync(
        Guid projectId,
        string query,
        int maxTokens = 8000,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Indexes a project's files for retrieval.
    /// </summary>
    /// <param name="projectId">Project ID</param>
    /// <param name="files">Dictionary of file paths to contents</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Indexing statistics</returns>
    Task<IndexingResult> IndexProjectAsync(
        Guid projectId,
        IDictionary<string, string> files,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Re-indexes only changed files.
    /// </summary>
    Task<IndexingResult> IncrementalIndexAsync(
        Guid projectId,
        IDictionary<string, string> files,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Indexes a project's files using streaming to minimize memory usage.
    /// Reads files on-demand from disk rather than loading all into memory.
    /// </summary>
    /// <param name="projectId">Project ID</param>
    /// <param name="workingDirectory">Directory containing source files</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Indexing statistics</returns>
    Task<IndexingResult> IndexProjectStreamingAsync(
        Guid projectId,
        string workingDirectory,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Repairs vectors in the vector store for a project by re-indexing DB-stored embeddings.
    /// </summary>
    Task RepairProjectVectorsAsync(Guid projectId, CancellationToken cancellationToken = default);
}

/// <summary>
/// Result from context retrieval.
/// </summary>
public record RetrievalResult
{
    /// <summary>
    /// Combined context text ready for LLM input
    /// </summary>
    public required string Context { get; init; }

    /// <summary>
    /// Total tokens in the context
    /// </summary>
    public required int TokenCount { get; init; }

    /// <summary>
    /// Source chunks used
    /// </summary>
    public required IReadOnlyList<SourceReference> Sources { get; init; }

    /// <summary>
    /// Whether hierarchical summarization was used
    /// </summary>
    public bool WasSummarized { get; init; }

    /// <summary>
    /// Number of raw chunks retrieved before summarization
    /// </summary>
    public int RawChunkCount { get; init; }

    /// <summary>
    /// Retrieval time in milliseconds
    /// </summary>
    public long RetrievalTimeMs { get; init; }
}

/// <summary>
/// Reference to a source chunk.
/// </summary>
public record SourceReference
{
    /// <summary>
    /// Chunk ID
    /// </summary>
    public required string ChunkId { get; init; }

    /// <summary>
    /// File path
    /// </summary>
    public required string FilePath { get; init; }

    /// <summary>
    /// Start line
    /// </summary>
    public required int StartLine { get; init; }

    /// <summary>
    /// End line
    /// </summary>
    public required int EndLine { get; init; }

    /// <summary>
    /// Similarity score
    /// </summary>
    public float Score { get; init; }

    /// <summary>
    /// Semantic type
    /// </summary>
    public string? SemanticType { get; init; }

    /// <summary>
    /// Semantic name
    /// </summary>
    public string? SemanticName { get; init; }
}

/// <summary>
/// Result from indexing operation.
/// </summary>
public record IndexingResult
{
    /// <summary>
    /// Number of files processed
    /// </summary>
    public required int FilesProcessed { get; init; }

    /// <summary>
    /// Number of chunks created
    /// </summary>
    public required int ChunksCreated { get; init; }

    /// <summary>
    /// Number of embeddings generated
    /// </summary>
    public required int EmbeddingsGenerated { get; init; }

    /// <summary>
    /// Total tokens in all chunks
    /// </summary>
    public required int TotalTokens { get; init; }

    /// <summary>
    /// Indexing time in milliseconds
    /// </summary>
    public required long IndexingTimeMs { get; init; }

    /// <summary>
    /// Number of chunks skipped (already indexed)
    /// </summary>
    public int ChunksSkipped { get; init; }

    /// <summary>
    /// Any errors encountered
    /// </summary>
    public IReadOnlyList<string>? Errors { get; init; }
}

/// <summary>
/// Model routing configuration
/// </summary>
public class ModelRouterOptions
{
    /// <summary>
    /// Configuration section name
    /// </summary>
    public const string SectionName = "ModelRouter";

    /// <summary>
    /// Small model for summarization (cheaper, faster)
    /// </summary>
    public string SmallModel { get; set; } = "gpt-4o-mini";

    /// <summary>
    /// Medium model for standard analysis
    /// </summary>
    public string MediumModel { get; set; } = "gpt-4o";

    /// <summary>
    /// Large model for complex synthesis
    /// </summary>
    public string LargeModel { get; set; } = "gpt-4o";

    /// <summary>
    /// Token threshold for switching to summarization
    /// </summary>
    public int SummarizationThreshold { get; set; } = 6000;

    /// <summary>
    /// Number of chunks per summarization bucket
    /// </summary>
    public int ChunksPerBucket { get; set; } = 10;

    /// <summary>
    /// Top K chunks to retrieve
    /// </summary>
    public int TopK { get; set; } = 20;

    /// <summary>
    /// Maximum cost per job (in USD, for approval workflow)
    /// </summary>
    public decimal MaxJobCost { get; set; } = 1.00m;
}
