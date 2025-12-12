// =============================================================================
// AAR.Infrastructure - Services/Memory/ConcurrencyLimiter.cs
// Bounded concurrency control using SemaphoreSlim
// =============================================================================

using AAR.Application.Configuration;
using AAR.Application.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AAR.Infrastructure.Services.Memory;

/// <summary>
/// Provides bounded concurrency control for API calls and processing.
/// </summary>
public class ConcurrencyLimiter : IConcurrencyLimiter, IDisposable
{
    private readonly SemaphoreSlim _embeddingSemaphore;
    private readonly SemaphoreSlim _reasoningSemaphore;
    private readonly SemaphoreSlim _fileReadSemaphore;
    private readonly ILogger<ConcurrencyLimiter> _logger;
    private readonly ConcurrencyOptions _options;
    private bool _disposed;

    public ConcurrencyLimiter(
        IOptions<ConcurrencyOptions> options,
        ILogger<ConcurrencyLimiter> logger)
    {
        _options = options.Value;
        _logger = logger;

        _embeddingSemaphore = new SemaphoreSlim(_options.EmbeddingConcurrency, _options.EmbeddingConcurrency);
        _reasoningSemaphore = new SemaphoreSlim(_options.ReasoningConcurrency, _options.ReasoningConcurrency);
        _fileReadSemaphore = new SemaphoreSlim(_options.FileReadConcurrency, _options.FileReadConcurrency);

        _logger.LogInformation(
            "ConcurrencyLimiter initialized: Embedding={Embedding}, Reasoning={Reasoning}, FileRead={FileRead}",
            _options.EmbeddingConcurrency, _options.ReasoningConcurrency, _options.FileReadConcurrency);
    }

    /// <inheritdoc/>
    public int EmbeddingQueueDepth => _options.EmbeddingConcurrency - _embeddingSemaphore.CurrentCount;

    /// <inheritdoc/>
    public int ReasoningQueueDepth => _options.ReasoningConcurrency - _reasoningSemaphore.CurrentCount;

    /// <inheritdoc/>
    public async Task<IDisposable> AcquireEmbeddingSlotAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Acquiring embedding slot (queue depth: {Depth})", EmbeddingQueueDepth);
        await _embeddingSemaphore.WaitAsync(cancellationToken);
        return new SemaphoreReleaser(_embeddingSemaphore);
    }

    /// <inheritdoc/>
    public async Task<IDisposable> AcquireReasoningSlotAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Acquiring reasoning slot (queue depth: {Depth})", ReasoningQueueDepth);
        await _reasoningSemaphore.WaitAsync(cancellationToken);
        return new SemaphoreReleaser(_reasoningSemaphore);
    }

    /// <inheritdoc/>
    public async Task<IDisposable> AcquireFileReadSlotAsync(CancellationToken cancellationToken = default)
    {
        await _fileReadSemaphore.WaitAsync(cancellationToken);
        return new SemaphoreReleaser(_fileReadSemaphore);
    }

    public void Dispose()
    {
        if (_disposed) return;

        _embeddingSemaphore.Dispose();
        _reasoningSemaphore.Dispose();
        _fileReadSemaphore.Dispose();

        _disposed = true;
    }

    /// <summary>
    /// Helper class to release semaphore on disposal.
    /// </summary>
    private sealed class SemaphoreReleaser : IDisposable
    {
        private readonly SemaphoreSlim _semaphore;
        private bool _disposed;

        public SemaphoreReleaser(SemaphoreSlim semaphore)
        {
            _semaphore = semaphore;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _semaphore.Release();
            _disposed = true;
        }
    }
}
