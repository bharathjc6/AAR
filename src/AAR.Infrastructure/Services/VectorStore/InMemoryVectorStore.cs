// =============================================================================
// AAR.Infrastructure - Services/VectorStore/InMemoryVectorStore.cs
// In-memory vector store with cosine similarity search
// =============================================================================

using System.Collections.Concurrent;
using System.Numerics;
using AAR.Application.Interfaces;
using Microsoft.Extensions.Logging;

namespace AAR.Infrastructure.Services.VectorStore;

/// <summary>
/// In-memory vector store using cosine similarity.
/// Suitable for development, testing, and small-scale deployments.
/// </summary>
public class InMemoryVectorStore : IVectorStore
{
    private readonly ConcurrentDictionary<string, VectorEntry> _vectors = new();
    private readonly ILogger<InMemoryVectorStore> _logger;

    public InMemoryVectorStore(ILogger<InMemoryVectorStore> logger)
    {
        _logger = logger;
        _logger.LogInformation("InMemoryVectorStore initialized");
    }

    /// <inheritdoc/>
    public Task IndexVectorAsync(
        string chunkId,
        float[] vector,
        VectorMetadata metadata,
        CancellationToken cancellationToken = default)
    {
        var normalizedVector = Normalize(vector);
        
        _vectors[chunkId] = new VectorEntry
        {
            ChunkId = chunkId,
            Vector = normalizedVector,
            Metadata = metadata
        };

        _logger.LogDebug("Indexed vector for chunk {ChunkId}", chunkId);
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task IndexVectorsAsync(
        IEnumerable<(string chunkId, float[] vector, VectorMetadata metadata)> vectors,
        CancellationToken cancellationToken = default)
    {
        var count = 0;
        foreach (var (chunkId, vector, metadata) in vectors)
        {
            cancellationToken.ThrowIfCancellationRequested();
            
            var normalizedVector = Normalize(vector);
            _vectors[chunkId] = new VectorEntry
            {
                ChunkId = chunkId,
                Vector = normalizedVector,
                Metadata = metadata
            };
            count++;
        }

        _logger.LogInformation("Indexed {Count} vectors", count);
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task<IReadOnlyList<VectorSearchResult>> QueryAsync(
        float[] queryVector,
        int topK = 10,
        Guid? projectId = null,
        CancellationToken cancellationToken = default)
    {
        var normalizedQuery = Normalize(queryVector);

        var results = _vectors.Values
            .Where(v => !projectId.HasValue || v.Metadata.ProjectId == projectId.Value)
            .Select(v => new
            {
                v.ChunkId,
                Score = CosineSimilarity(normalizedQuery, v.Vector),
                v.Metadata
            })
            .OrderByDescending(r => r.Score)
            .Take(topK)
            .Select(r => new VectorSearchResult
            {
                ChunkId = r.ChunkId,
                Score = r.Score,
                Metadata = r.Metadata
            })
            .ToList();

        _logger.LogDebug("Query returned {Count} results", results.Count);
        return Task.FromResult<IReadOnlyList<VectorSearchResult>>(results);
    }

    /// <inheritdoc/>
    public Task DeleteByProjectIdAsync(Guid projectId, CancellationToken cancellationToken = default)
    {
        var toRemove = _vectors
            .Where(kv => kv.Value.Metadata.ProjectId == projectId)
            .Select(kv => kv.Key)
            .ToList();

        foreach (var key in toRemove)
        {
            _vectors.TryRemove(key, out _);
        }

        _logger.LogInformation("Deleted {Count} vectors for project {ProjectId}", toRemove.Count, projectId);
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task DeleteAsync(string chunkId, CancellationToken cancellationToken = default)
    {
        _vectors.TryRemove(chunkId, out _);
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task<int> CountAsync(Guid? projectId = null, CancellationToken cancellationToken = default)
    {
        var count = projectId.HasValue
            ? _vectors.Values.Count(v => v.Metadata.ProjectId == projectId.Value)
            : _vectors.Count;

        return Task.FromResult(count);
    }

    /// <summary>
    /// Computes cosine similarity between two vectors.
    /// Both vectors should be pre-normalized for best performance.
    /// </summary>
    private static float CosineSimilarity(float[] a, float[] b)
    {
        if (a.Length != b.Length)
            return 0f;

        // Use SIMD for performance if available
        if (Vector.IsHardwareAccelerated && a.Length >= Vector<float>.Count)
        {
            return CosineSimilaritySimd(a, b);
        }

        var dotProduct = 0f;
        for (var i = 0; i < a.Length; i++)
        {
            dotProduct += a[i] * b[i];
        }

        return dotProduct; // Vectors are pre-normalized, so this is the cosine similarity
    }

    private static float CosineSimilaritySimd(float[] a, float[] b)
    {
        var dotProduct = 0f;
        var vectorSize = Vector<float>.Count;
        var i = 0;

        // Process in SIMD chunks
        for (; i <= a.Length - vectorSize; i += vectorSize)
        {
            var va = new Vector<float>(a, i);
            var vb = new Vector<float>(b, i);
            dotProduct += Vector.Dot(va, vb);
        }

        // Handle remaining elements
        for (; i < a.Length; i++)
        {
            dotProduct += a[i] * b[i];
        }

        return dotProduct;
    }

    /// <summary>
    /// Normalizes a vector to unit length.
    /// </summary>
    private static float[] Normalize(float[] vector)
    {
        var magnitude = MathF.Sqrt(vector.Sum(x => x * x));
        
        if (magnitude == 0)
            return vector;

        var normalized = new float[vector.Length];
        for (var i = 0; i < vector.Length; i++)
        {
            normalized[i] = vector[i] / magnitude;
        }

        return normalized;
    }

    private record VectorEntry
    {
        public required string ChunkId { get; init; }
        public required float[] Vector { get; init; }
        public required VectorMetadata Metadata { get; init; }
    }
}
