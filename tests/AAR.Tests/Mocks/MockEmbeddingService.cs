// AAR.Tests - Mocks/MockEmbeddingService.cs
// Mock implementation of embedding service for testing

using AAR.Application.Interfaces;
using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;

namespace AAR.Tests.Mocks;

/// <summary>
/// Mock embedding service that returns deterministic vectors for testing.
/// </summary>
public class MockEmbeddingService : IEmbeddingService
{
    private readonly ConcurrentBag<EmbeddingCall> _calls = new();
    private readonly ConcurrentDictionary<string, float[]> _cache = new();
    private int _delay;
    private int _dimensions = 1536;

    public IReadOnlyCollection<EmbeddingCall> Calls => _calls.ToArray();
    public int CallCount => _calls.Count;
    public string ModelName => "mock-embedding-model";
    public int Dimension => _dimensions;
    public bool IsMock => true;

    public void SetDelay(int milliseconds) => _delay = milliseconds;
    public void SetDimensions(int dimensions) => _dimensions = dimensions;
    public void Reset() { _calls.Clear(); _cache.Clear(); _delay = 0; }

    public async Task<float[]> CreateEmbeddingAsync(string text, CancellationToken cancellationToken = default)
    {
        if (_delay > 0) await Task.Delay(_delay, cancellationToken);
        _calls.Add(new EmbeddingCall { Text = text, IsBatch = false, Timestamp = DateTime.UtcNow });
        return _cache.GetOrAdd(text, GenerateEmbedding);
    }

    public async Task<IReadOnlyList<float[]>> CreateEmbeddingsAsync(
        IEnumerable<string> texts,
        CancellationToken cancellationToken = default)
    {
        if (_delay > 0) await Task.Delay(_delay, cancellationToken);
        var textList = texts.ToList();
        _calls.Add(new EmbeddingCall { Text = "batch", IsBatch = true, BatchSize = textList.Count, Timestamp = DateTime.UtcNow });
        return textList.Select(t => _cache.GetOrAdd(t, GenerateEmbedding)).ToList();
    }

    private float[] GenerateEmbedding(string text)
    {
        var embedding = new float[_dimensions];
        using var sha256 = SHA256.Create();
        var hash = sha256.ComputeHash(Encoding.UTF8.GetBytes(text));
        var random = new Random(BitConverter.ToInt32(hash, 0));
        for (int i = 0; i < _dimensions; i++)
            embedding[i] = (float)(random.NextDouble() * 2 - 1);
        var magnitude = (float)Math.Sqrt(embedding.Sum(x => x * x));
        for (int i = 0; i < _dimensions; i++)
            embedding[i] /= magnitude;
        return embedding;
    }
}

public record EmbeddingCall
{
    public required string Text { get; init; }
    public bool IsBatch { get; init; }
    public int BatchSize { get; init; }
    public DateTime Timestamp { get; init; }
}
