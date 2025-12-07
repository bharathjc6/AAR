// =============================================================================
// AAR.Infrastructure - Services/Embedding/ResilientEmbeddingService.cs
// Embedding service wrapper with Polly resilience
// =============================================================================

using AAR.Application.Configuration;
using AAR.Application.Interfaces;
using AAR.Infrastructure.Services.Resilience;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Polly;
using Polly.Registry;

namespace AAR.Infrastructure.Services.Embedding;

/// <summary>
/// Decorator that adds resilience (retry, circuit breaker) to embedding calls
/// </summary>
public sealed class ResilientEmbeddingService : IEmbeddingService
{
    private readonly IEmbeddingService _innerService;
    private readonly ResiliencePipeline _pipeline;
    private readonly EmbeddingProcessingOptions _options;
    private readonly IMetricsService _metrics;
    private readonly ILogger<ResilientEmbeddingService> _logger;
    
    // Rate limiting state
    private readonly SemaphoreSlim _rateLimiter;
    private DateTime _periodStart = DateTime.UtcNow;
    private long _tokensThisPeriod;
    private readonly object _rateLock = new();

    public string ModelName => _innerService.ModelName;
    public int Dimension => _innerService.Dimension;
    public bool IsMock => _innerService.IsMock;

    public ResilientEmbeddingService(
        IEmbeddingService innerService,
        ResiliencePipelineProvider<string> pipelineProvider,
        IOptions<EmbeddingProcessingOptions> options,
        IMetricsService metrics,
        ILogger<ResilientEmbeddingService> logger)
    {
        _innerService = innerService;
        _pipeline = pipelineProvider.GetPipeline(ResiliencePipelineNames.EmbeddingApi);
        _options = options.Value;
        _metrics = metrics;
        _logger = logger;
        _rateLimiter = new SemaphoreSlim(_options.EmbeddingConcurrency, _options.EmbeddingConcurrency);
    }

    public async Task<float[]> CreateEmbeddingAsync(
        string text, 
        CancellationToken cancellationToken = default)
    {
        await WaitForRateLimitAsync(EstimateTokens(text), cancellationToken);
        
        try
        {
            await _rateLimiter.WaitAsync(cancellationToken);
            
            var result = await _pipeline.ExecuteAsync(
                async ct => await _innerService.CreateEmbeddingAsync(text, ct),
                cancellationToken);

            RecordTokenUsage(EstimateTokens(text));
            return result;
        }
        finally
        {
            _rateLimiter.Release();
        }
    }

    public async Task<IReadOnlyList<float[]>> CreateEmbeddingsAsync(
        IEnumerable<string> texts, 
        CancellationToken cancellationToken = default)
    {
        var textList = texts.ToList();
        if (textList.Count == 0)
            return [];

        var totalTokens = textList.Sum(EstimateTokens);
        await WaitForRateLimitAsync(totalTokens, cancellationToken);

        try
        {
            await _rateLimiter.WaitAsync(cancellationToken);

            var result = await _pipeline.ExecuteAsync(
                async ct => await _innerService.CreateEmbeddingsAsync(textList, ct),
                cancellationToken);

            RecordTokenUsage(totalTokens);
            _logger.LogDebug(
                "Generated {Count} embeddings, ~{Tokens} tokens",
                result.Count, totalTokens);

            return result;
        }
        finally
        {
            _rateLimiter.Release();
        }
    }

    /// <summary>
    /// Processes embeddings in batches with rate limiting
    /// </summary>
    public async Task<IReadOnlyList<float[]>> CreateEmbeddingsBatchedAsync(
        IEnumerable<string> texts,
        int batchSize,
        IProgress<int>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var textList = texts.ToList();
        var results = new List<float[]>();
        var processed = 0;

        foreach (var batch in textList.Chunk(batchSize))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var batchResults = await CreateEmbeddingsAsync(batch, cancellationToken);
            results.AddRange(batchResults);

            processed += batch.Length;
            progress?.Report(processed);

            _logger.LogDebug(
                "Batch complete: {Processed}/{Total} texts",
                processed, textList.Count);
        }

        return results;
    }

    private async Task WaitForRateLimitAsync(int estimatedTokens, CancellationToken cancellationToken)
    {
        while (true)
        {
            lock (_rateLock)
            {
                // Reset period if minute has passed
                var now = DateTime.UtcNow;
                if ((now - _periodStart).TotalMinutes >= 1)
                {
                    _periodStart = now;
                    _tokensThisPeriod = 0;
                }

                // Check if we have capacity
                if (_tokensThisPeriod + estimatedTokens <= _options.EmbeddingTokensPerMinute)
                {
                    return; // Proceed immediately
                }
            }

            // Wait and retry
            var waitTime = TimeSpan.FromSeconds(5);
            _logger.LogDebug(
                "Rate limited: {Used}/{Limit} tokens/min. Waiting {Wait}s",
                _tokensThisPeriod, _options.EmbeddingTokensPerMinute, waitTime.TotalSeconds);

            await Task.Delay(waitTime, cancellationToken);
        }
    }

    private void RecordTokenUsage(int tokens)
    {
        lock (_rateLock)
        {
            _tokensThisPeriod += tokens;
        }

        _metrics.RecordTokensConsumed(ModelName, tokens, "system");
    }

    private static int EstimateTokens(string text)
    {
        // Rough estimate: ~4 chars per token for English text
        return Math.Max(1, text.Length / 4);
    }
}
