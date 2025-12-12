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
        
        var acquired = false;
        try
        {
            // IMPROVED: Reduced from 2 minutes to 30 seconds to prevent long stalls
            acquired = await _rateLimiter.WaitAsync(TimeSpan.FromSeconds(30), cancellationToken);
            if (!acquired)
            {
                _logger.LogWarning("Single embedding semaphore wait timed out after 30s. Proceeding anyway.");
            }
            
            var result = await _pipeline.ExecuteAsync(
                async ct => await _innerService.CreateEmbeddingAsync(text, ct),
                cancellationToken);

            RecordTokenUsage(EstimateTokens(text));
            return result;
        }
        finally
        {
            if (acquired)
            {
                _rateLimiter.Release();
            }
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

        var acquired = false;
        try
        {
            // IMPROVED: Reduced from 2 minutes to 30 seconds to prevent long stalls
            // Stuck batch processing at batch 21/33 was caused by indefinite semaphore waits
            acquired = await _rateLimiter.WaitAsync(TimeSpan.FromSeconds(30), cancellationToken);
            if (!acquired)
            {
                _logger.LogWarning("Batch embedding semaphore wait timed out after 30s. Proceeding without semaphore.");
            }

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
            if (acquired)
            {
                _rateLimiter.Release();
            }
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
        // IMPROVED: Reduced max wait to 30 seconds to prevent long stalls
        // Previous 2-minute wait caused stuck batch processing in production
        const int maxWaitIterations = 30; // Max 30 seconds of waiting (30 x 1s)
        var waitIterations = 0;
        var startTime = DateTime.UtcNow;
        
        while (waitIterations < maxWaitIterations)
        {
            cancellationToken.ThrowIfCancellationRequested();
            
            lock (_rateLock)
            {
                // Reset period if minute has passed
                var now = DateTime.UtcNow;
                if ((now - _periodStart).TotalMinutes >= 1)
                {
                    _periodStart = now;
                    _tokensThisPeriod = 0;
                    _logger.LogDebug("Rate limit period reset. New period started.");
                }

                // If estimated tokens alone exceed the limit, proceed anyway after waiting for period reset
                // This prevents infinite loops for large batches
                if (estimatedTokens > _options.EmbeddingTokensPerMinute)
                {
                    _logger.LogWarning(
                        "Batch tokens ({EstimatedTokens}) exceed per-minute limit ({Limit}). Proceeding anyway.",
                        estimatedTokens, _options.EmbeddingTokensPerMinute);
                    _tokensThisPeriod = estimatedTokens;
                    return;
                }
                
                // Check if we have capacity
                if (_tokensThisPeriod + estimatedTokens <= _options.EmbeddingTokensPerMinute)
                {
                    // Reserve tokens upfront to prevent over-commitment
                    _tokensThisPeriod += estimatedTokens;
                    return; // Proceed immediately
                }
            }

            // Calculate time until period reset for smarter waiting
            TimeSpan waitTime;
            lock (_rateLock)
            {
                var elapsed = DateTime.UtcNow - _periodStart;
                var remainingUntilReset = TimeSpan.FromMinutes(1) - elapsed;
                // Wait until period resets, or max 1 second if something went wrong
                waitTime = remainingUntilReset > TimeSpan.Zero 
                    ? TimeSpan.FromSeconds(Math.Min(remainingUntilReset.TotalSeconds + 0.5, 1))
                    : TimeSpan.FromSeconds(1);
            }

            if (waitIterations % 5 == 0) // Log every 5 seconds (more frequent to aid debugging)
            {
                _logger.LogDebug(
                    "Rate limited: {Used}/{Limit} tokens/min. Waiting {Wait:F1}s (iteration {Iteration}/{Max}, elapsed {Elapsed:F1}s)",
                    _tokensThisPeriod, _options.EmbeddingTokensPerMinute, waitTime.TotalSeconds, 
                    waitIterations, maxWaitIterations, (DateTime.UtcNow - startTime).TotalSeconds);
            }

            await Task.Delay(waitTime, cancellationToken);
            waitIterations++;
        }
        
        // Max wait exceeded, proceed anyway to prevent deadlock
        _logger.LogWarning(
            "Rate limit max wait exceeded after {Iterations} iterations ({Elapsed:F1}s). Proceeding with {Tokens} tokens.",
            waitIterations, (DateTime.UtcNow - startTime).TotalSeconds, estimatedTokens);
        
        lock (_rateLock)
        {
            // Reset period since we're forcing through
            _periodStart = DateTime.UtcNow;
            _tokensThisPeriod = estimatedTokens;
        }
    }

    private void RecordTokenUsage(int tokens)
    {
        // Note: Tokens are already reserved upfront in WaitForRateLimitAsync
        // This method only records metrics
        _metrics.RecordTokensConsumed(ModelName, tokens, "system");
    }

    private static int EstimateTokens(string text)
    {
        // Rough estimate: ~4 chars per token for English text
        return Math.Max(1, text.Length / 4);
    }
}
