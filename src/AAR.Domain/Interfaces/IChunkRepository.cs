// =============================================================================
// AAR.Domain - Interfaces/IChunkRepository.cs
// Repository interface for chunk operations
// =============================================================================

using AAR.Domain.Entities;

namespace AAR.Domain.Interfaces;

/// <summary>
/// Repository for chunk operations
/// </summary>
public interface IChunkRepository
{
    /// <summary>
    /// Gets all chunks for a project
    /// </summary>
    Task<IReadOnlyList<Chunk>> GetByProjectIdAsync(
        Guid projectId, 
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a chunk by its hash
    /// </summary>
    Task<Chunk?> GetByHashAsync(
        string chunkHash, 
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets chunks by file path
    /// </summary>
    Task<IReadOnlyList<Chunk>> GetByFilePathAsync(
        Guid projectId, 
        string filePath, 
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets chunks that have embeddings
    /// </summary>
    Task<IReadOnlyList<Chunk>> GetWithEmbeddingsAsync(
        Guid projectId, 
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets chunks that need embeddings
    /// </summary>
    Task<IReadOnlyList<Chunk>> GetWithoutEmbeddingsAsync(
        Guid projectId, 
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Adds a chunk
    /// </summary>
    Task<Chunk> AddAsync(Chunk chunk, CancellationToken cancellationToken = default);

    /// <summary>
    /// Adds multiple chunks
    /// </summary>
    Task AddRangeAsync(IEnumerable<Chunk> chunks, CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates a chunk
    /// </summary>
    void Update(Chunk chunk);

    /// <summary>
    /// Deletes chunks for a project
    /// </summary>
    Task DeleteByProjectIdAsync(Guid projectId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes chunks for a file
    /// </summary>
    Task DeleteByFilePathAsync(
        Guid projectId, 
        string filePath, 
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks if a chunk with the given hash exists
    /// </summary>
    Task<bool> ExistsAsync(string chunkHash, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets chunk count for a project
    /// </summary>
    Task<int> CountByProjectIdAsync(Guid projectId, CancellationToken cancellationToken = default);
}
