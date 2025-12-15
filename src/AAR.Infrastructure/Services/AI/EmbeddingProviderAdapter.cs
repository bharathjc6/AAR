// =============================================================================
// AAR.Infrastructure - Services/AI/EmbeddingProviderAdapter.cs
// Adapts IEmbeddingProvider to IEmbeddingService for backward compatibility
// =============================================================================

using AAR.Application.Interfaces;
using Microsoft.Extensions.Logging;

namespace AAR.Infrastructure.Services.AI;

/// <summary>
/// Adapts the new IEmbeddingProvider interface to the existing IEmbeddingService interface
/// This maintains backward compatibility while using the new provider architecture
/// </summary>
public class EmbeddingProviderAdapter : IEmbeddingService
{
    private readonly IEmbeddingProvider _provider;
    private readonly ILogger<EmbeddingProviderAdapter> _logger;

    public string ModelName => _provider.ModelName;
    public int Dimension => _provider.Dimension;
    public bool IsMock => false;

    public EmbeddingProviderAdapter(
        IEmbeddingProvider provider,
        ILogger<EmbeddingProviderAdapter> logger)
    {
        _provider = provider;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task<float[]> CreateEmbeddingAsync(
        string text,
        CancellationToken cancellationToken = default)
    {
        try
        {
            return await _provider.GenerateAsync(text, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create embedding");
            throw;
        }
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<float[]>> CreateEmbeddingsAsync(
        IEnumerable<string> texts,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var textList = texts.ToList();
            return await _provider.GenerateBatchAsync(textList, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create embeddings batch");
            throw;
        }
    }
}
