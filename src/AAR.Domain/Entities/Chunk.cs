// =============================================================================
// AAR.Domain - Entities/Chunk.cs
// Represents a code chunk for vector storage and retrieval
// =============================================================================

namespace AAR.Domain.Entities;

/// <summary>
/// Represents a semantic chunk of code or text for vector-based retrieval.
/// </summary>
public class Chunk : BaseEntity
{
    /// <summary>
    /// Deterministic chunk ID (SHA256 hash of path + content)
    /// </summary>
    public string ChunkHash { get; private set; } = string.Empty;

    /// <summary>
    /// Project this chunk belongs to
    /// </summary>
    public Guid ProjectId { get; private set; }

    /// <summary>
    /// File path relative to project root
    /// </summary>
    public string FilePath { get; private set; } = string.Empty;

    /// <summary>
    /// Starting line number (1-based)
    /// </summary>
    public int StartLine { get; private set; }

    /// <summary>
    /// Ending line number (1-based, inclusive)
    /// </summary>
    public int EndLine { get; private set; }

    /// <summary>
    /// Token count for this chunk
    /// </summary>
    public int TokenCount { get; private set; }

    /// <summary>
    /// Detected programming language
    /// </summary>
    public string Language { get; private set; } = string.Empty;

    /// <summary>
    /// Hash of the text content (for change detection)
    /// </summary>
    public string TextHash { get; private set; } = string.Empty;

    /// <summary>
    /// Semantic type of the chunk (namespace, class, method, etc.)
    /// </summary>
    public string? SemanticType { get; private set; }

    /// <summary>
    /// Name of the semantic element (class name, method name, etc.)
    /// </summary>
    public string? SemanticName { get; private set; }

    /// <summary>
    /// The actual text content (optional, controlled by StoreChunkText config)
    /// </summary>
    public string? Content { get; private set; }

    /// <summary>
    /// Index of this chunk within its semantic unit (0-based)
    /// </summary>
    public int ChunkIndex { get; private set; }

    /// <summary>
    /// Total number of chunks in the semantic unit
    /// </summary>
    public int TotalChunks { get; private set; }

    /// <summary>
    /// Embedding vector (serialized as JSON array of floats)
    /// </summary>
    public string? EmbeddingJson { get; private set; }

    /// <summary>
    /// Model used to generate the embedding
    /// </summary>
    public string? EmbeddingModel { get; private set; }

    /// <summary>
    /// When the embedding was generated
    /// </summary>
    public DateTime? EmbeddingGeneratedAt { get; private set; }

    /// <summary>
    /// Navigation property to the project
    /// </summary>
    public Project? Project { get; private set; }

    // Private constructor for EF Core
    private Chunk() { }

    /// <summary>
    /// Creates a new chunk
    /// </summary>
    public static Chunk Create(
        Guid projectId,
        string filePath,
        int startLine,
        int endLine,
        int tokenCount,
        string language,
        string textHash,
        string chunkHash,
        string? semanticType = null,
        string? semanticName = null,
        string? content = null,
        int chunkIndex = 0,
        int totalChunks = 1)
    {
        return new Chunk
        {
            Id = Guid.NewGuid(),
            ProjectId = projectId,
            FilePath = filePath,
            StartLine = startLine,
            EndLine = endLine,
            TokenCount = tokenCount,
            Language = language,
            TextHash = textHash,
            ChunkHash = chunkHash,
            SemanticType = semanticType,
            SemanticName = semanticName,
            Content = content,
            ChunkIndex = chunkIndex,
            TotalChunks = totalChunks,
            CreatedAt = DateTime.UtcNow
        };
    }

    /// <summary>
    /// Sets the embedding for this chunk
    /// </summary>
    public void SetEmbedding(float[] embedding, string model)
    {
        EmbeddingJson = System.Text.Json.JsonSerializer.Serialize(embedding);
        EmbeddingModel = model;
        EmbeddingGeneratedAt = DateTime.UtcNow;
        SetUpdated();
    }

    /// <summary>
    /// Gets the embedding as a float array
    /// </summary>
    public float[]? GetEmbedding()
    {
        if (string.IsNullOrEmpty(EmbeddingJson))
            return null;

        return System.Text.Json.JsonSerializer.Deserialize<float[]>(EmbeddingJson);
    }

    /// <summary>
    /// Clears the content to save space (if StoreChunkText is disabled)
    /// </summary>
    public void ClearContent()
    {
        Content = null;
        SetUpdated();
    }
}
