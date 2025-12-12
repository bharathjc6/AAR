// AAR.Tests - Mocks/InMemoryVectorStore.cs
// In-memory vector store for testing

using AAR.Application.Interfaces;
using System.Collections.Concurrent;

namespace AAR.Tests.Mocks;

/// <summary>
/// In-memory vector store implementation for testing.
/// </summary>
public class InMemoryVectorStore : IVectorStore
{
    private readonly ConcurrentDictionary<string, VectorEntry> _vectors = new();
    private readonly ConcurrentBag<VectorOperation> _operations = new();

    public IReadOnlyCollection<VectorOperation> Operations => _operations.ToArray();
    public int VectorCount => _vectors.Count;
    public void Reset() { _vectors.Clear(); _operations.Clear(); }

    public Task IndexVectorAsync(string chunkId, float[] vector, VectorMetadata metadata, CancellationToken cancellationToken = default)
    {
        _vectors[chunkId] = new VectorEntry { ChunkId = chunkId, Vector = vector, Metadata = metadata };
        _operations.Add(new VectorOperation { Type = "IndexVector", ChunkId = chunkId, ProjectId = metadata.ProjectId });
        return Task.CompletedTask;
    }

    public Task IndexVectorsAsync(IEnumerable<(string chunkId, float[] vector, VectorMetadata metadata)> vectors, CancellationToken cancellationToken = default)
    {
        var vectorList = vectors.ToList();
        foreach (var (chunkId, vector, metadata) in vectorList)
        {
            _vectors[chunkId] = new VectorEntry { ChunkId = chunkId, Vector = vector, Metadata = metadata };
        }
        _operations.Add(new VectorOperation { Type = "IndexVectorsBatch", BatchSize = vectorList.Count });
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<VectorSearchResult>> QueryAsync(float[] queryVector, int topK = 10, Guid? projectId = null, CancellationToken cancellationToken = default)
    {
        var results = _vectors.Values
            .Where(v => projectId == null || v.Metadata.ProjectId == projectId)
            .Select(v => new { Entry = v, Score = CosineSimilarity(v.Vector, queryVector) })
            .OrderByDescending(x => x.Score)
            .Take(topK)
            .Select(x => new VectorSearchResult
            {
                ChunkId = x.Entry.ChunkId,
                Score = x.Score,
                Metadata = x.Entry.Metadata
            })
            .ToList();

        _operations.Add(new VectorOperation { Type = "Query", ProjectId = projectId ?? Guid.Empty, ResultCount = results.Count });
        return Task.FromResult<IReadOnlyList<VectorSearchResult>>(results);
    }

    public Task DeleteByProjectIdAsync(Guid projectId, CancellationToken cancellationToken = default)
    {
        var keysToRemove = _vectors.Where(kv => kv.Value.Metadata.ProjectId == projectId).Select(kv => kv.Key).ToList();
        foreach (var key in keysToRemove) _vectors.TryRemove(key, out _);
        _operations.Add(new VectorOperation { Type = "DeleteByProject", ProjectId = projectId });
        return Task.CompletedTask;
    }

    public Task DeleteAsync(string chunkId, CancellationToken cancellationToken = default)
    {
        _vectors.TryRemove(chunkId, out _);
        _operations.Add(new VectorOperation { Type = "Delete", ChunkId = chunkId });
        return Task.CompletedTask;
    }

    public Task<int> CountAsync(Guid? projectId = null, CancellationToken cancellationToken = default)
    {
        var count = projectId == null 
            ? _vectors.Count 
            : _vectors.Values.Count(v => v.Metadata.ProjectId == projectId);
        return Task.FromResult(count);
    }

    private static float CosineSimilarity(float[] a, float[] b)
    {
        if (a.Length != b.Length) return 0;
        float dotProduct = 0, magA = 0, magB = 0;
        for (int i = 0; i < a.Length; i++)
        {
            dotProduct += a[i] * b[i];
            magA += a[i] * a[i];
            magB += b[i] * b[i];
        }
        if (magA == 0 || magB == 0) return 0;
        return dotProduct / (float)(Math.Sqrt(magA) * Math.Sqrt(magB));
    }
}

public record VectorEntry
{
    public required string ChunkId { get; init; }
    public required float[] Vector { get; init; }
    public required VectorMetadata Metadata { get; init; }
}

public record VectorOperation
{
    public required string Type { get; init; }
    public string? ChunkId { get; init; }
    public Guid ProjectId { get; init; }
    public int BatchSize { get; init; }
    public int ResultCount { get; init; }
}
