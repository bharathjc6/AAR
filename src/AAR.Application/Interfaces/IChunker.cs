// =============================================================================
// AAR.Application - Interfaces/IChunker.cs
// Interface for code chunking operations
// =============================================================================

namespace AAR.Application.Interfaces;

/// <summary>
/// Interface for chunking code files into semantically meaningful segments.
/// </summary>
public interface IChunker
{
    /// <summary>
    /// Chunks a single file into semantic segments.
    /// </summary>
    /// <param name="filePath">Path to the file</param>
    /// <param name="content">File content</param>
    /// <param name="projectId">Project ID for chunk association</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>List of chunks</returns>
    Task<IReadOnlyList<ChunkInfo>> ChunkFileAsync(
        string filePath,
        string content,
        Guid projectId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Chunks multiple files.
    /// </summary>
    /// <param name="files">Dictionary of file paths to contents</param>
    /// <param name="projectId">Project ID for chunk association</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>List of all chunks</returns>
    Task<IReadOnlyList<ChunkInfo>> ChunkFilesAsync(
        IDictionary<string, string> files,
        Guid projectId,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Represents a chunk of code/text with metadata.
/// </summary>
public record ChunkInfo
{
    /// <summary>
    /// Deterministic chunk ID (SHA256 hash of path + content)
    /// </summary>
    public required string ChunkHash { get; init; }

    /// <summary>
    /// Project ID
    /// </summary>
    public required Guid ProjectId { get; init; }

    /// <summary>
    /// File path relative to project root
    /// </summary>
    public required string FilePath { get; init; }

    /// <summary>
    /// Starting line number (1-based)
    /// </summary>
    public required int StartLine { get; init; }

    /// <summary>
    /// Ending line number (1-based, inclusive)
    /// </summary>
    public required int EndLine { get; init; }

    /// <summary>
    /// Token count
    /// </summary>
    public required int TokenCount { get; init; }

    /// <summary>
    /// Programming language
    /// </summary>
    public required string Language { get; init; }

    /// <summary>
    /// Hash of the text content
    /// </summary>
    public required string TextHash { get; init; }

    /// <summary>
    /// The actual text content
    /// </summary>
    public required string Content { get; init; }

    /// <summary>
    /// Semantic type (namespace, class, method, etc.)
    /// </summary>
    public string? SemanticType { get; init; }

    /// <summary>
    /// Semantic name (class name, method name, etc.)
    /// </summary>
    public string? SemanticName { get; init; }
}

/// <summary>
/// Chunker configuration options
/// </summary>
public class ChunkerOptions
{
    /// <summary>
    /// Configuration section name
    /// </summary>
    public const string SectionName = "Chunker";

    /// <summary>
    /// Maximum tokens per chunk (default: 1600)
    /// </summary>
    public int MaxChunkTokens { get; set; } = 1600;

    /// <summary>
    /// Overlap tokens between chunks (default: 200)
    /// </summary>
    public int OverlapTokens { get; set; } = 200;

    /// <summary>
    /// Whether to store chunk text in database
    /// </summary>
    public bool StoreChunkText { get; set; } = true;

    /// <summary>
    /// Use semantic splitting for supported languages (C#, etc.)
    /// </summary>
    public bool UseSemanticSplitting { get; set; } = true;

    /// <summary>
    /// Minimum chunk size in tokens
    /// </summary>
    public int MinChunkTokens { get; set; } = 50;
}
