// =============================================================================
// AAR.Tests - Infrastructure/ConcurrencyLimiterTests.cs
// Unit tests for bounded concurrency control
// =============================================================================

using AAR.Application.Configuration;
using AAR.Infrastructure.Services.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using System.Diagnostics;

namespace AAR.Tests.Infrastructure;

public class ConcurrencyLimiterTests
{
    private readonly Mock<ILogger<ConcurrencyLimiter>> _loggerMock;

    public ConcurrencyLimiterTests()
    {
        _loggerMock = new Mock<ILogger<ConcurrencyLimiter>>();
    }

    private ConcurrencyLimiter CreateLimiter(
        int embeddingConcurrency = 4, 
        int reasoningConcurrency = 2,
        int fileReadConcurrency = 8)
    {
        var options = Options.Create(new ConcurrencyOptions
        {
            EmbeddingConcurrency = embeddingConcurrency,
            ReasoningConcurrency = reasoningConcurrency,
            FileReadConcurrency = fileReadConcurrency
        });

        return new ConcurrencyLimiter(options, _loggerMock.Object);
    }

    [Fact]
    public void EmbeddingQueueDepth_Initial_ShouldBeZero()
    {
        // Arrange
        var limiter = CreateLimiter(embeddingConcurrency: 4);

        // Act
        var depth = limiter.EmbeddingQueueDepth;

        // Assert
        Assert.Equal(0, depth);
    }

    [Fact]
    public void ReasoningQueueDepth_Initial_ShouldBeZero()
    {
        // Arrange
        var limiter = CreateLimiter(reasoningConcurrency: 3);

        // Act
        var depth = limiter.ReasoningQueueDepth;

        // Assert
        Assert.Equal(0, depth);
    }

    [Fact]
    public async Task AcquireEmbeddingSlotAsync_ShouldIncrementQueueDepth()
    {
        // Arrange
        var limiter = CreateLimiter(embeddingConcurrency: 4);

        // Act
        using var slot = await limiter.AcquireEmbeddingSlotAsync();
        var depth = limiter.EmbeddingQueueDepth;

        // Assert
        Assert.Equal(1, depth);
    }

    [Fact]
    public async Task AcquireEmbeddingSlotAsync_WhenDisposed_ShouldDecrementQueueDepth()
    {
        // Arrange
        var limiter = CreateLimiter(embeddingConcurrency: 4);

        // Act
        var slot = await limiter.AcquireEmbeddingSlotAsync();
        Assert.Equal(1, limiter.EmbeddingQueueDepth);
        
        slot.Dispose();
        var depth = limiter.EmbeddingQueueDepth;

        // Assert
        Assert.Equal(0, depth);
    }

    [Fact]
    public async Task AcquireReasoningSlotAsync_ShouldIncrementQueueDepth()
    {
        // Arrange
        var limiter = CreateLimiter(reasoningConcurrency: 2);

        // Act
        using var slot = await limiter.AcquireReasoningSlotAsync();
        var depth = limiter.ReasoningQueueDepth;

        // Assert
        Assert.Equal(1, depth);
    }

    [Fact]
    public async Task AcquireReasoningSlotAsync_WhenDisposed_ShouldDecrementQueueDepth()
    {
        // Arrange
        var limiter = CreateLimiter(reasoningConcurrency: 2);

        // Act
        var slot = await limiter.AcquireReasoningSlotAsync();
        Assert.Equal(1, limiter.ReasoningQueueDepth);
        
        slot.Dispose();
        var depth = limiter.ReasoningQueueDepth;

        // Assert
        Assert.Equal(0, depth);
    }

    [Fact]
    public async Task AcquireMultipleEmbeddingSlots_ShouldReflectInQueueDepth()
    {
        // Arrange
        var limiter = CreateLimiter(embeddingConcurrency: 4);

        // Act
        using var slot1 = await limiter.AcquireEmbeddingSlotAsync();
        using var slot2 = await limiter.AcquireEmbeddingSlotAsync();

        // Assert
        Assert.Equal(2, limiter.EmbeddingQueueDepth);
    }

    [Fact]
    public async Task AcquireAllEmbeddingSlots_ShouldMaxOutQueueDepth()
    {
        // Arrange
        var limiter = CreateLimiter(embeddingConcurrency: 2);

        // Act
        using var slot1 = await limiter.AcquireEmbeddingSlotAsync();
        using var slot2 = await limiter.AcquireEmbeddingSlotAsync();

        // Assert - Queue depth equals concurrency limit
        Assert.Equal(2, limiter.EmbeddingQueueDepth);
    }

    [Fact]
    public async Task AcquireEmbeddingSlot_WhenNoSlots_ShouldWait()
    {
        // Arrange
        var limiter = CreateLimiter(embeddingConcurrency: 1);
        var sw = Stopwatch.StartNew();

        // Act
        var slot1 = await limiter.AcquireEmbeddingSlotAsync();
        
        var acquireTask = limiter.AcquireEmbeddingSlotAsync(CancellationToken.None);
        
        // Slot1 is held, acquireTask should be waiting
        await Task.Delay(50);
        Assert.False(acquireTask.IsCompleted);

        // Release slot1
        slot1.Dispose();
        
        // Now acquireTask should complete
        using var slot2 = await acquireTask;
        sw.Stop();

        // Assert
        Assert.True(sw.ElapsedMilliseconds >= 40); // Had to wait
    }

    [Fact]
    public async Task AcquireEmbeddingSlot_WithCancellation_ShouldThrow()
    {
        // Arrange
        var limiter = CreateLimiter(embeddingConcurrency: 1);
        using var cts = new CancellationTokenSource();
        
        // Acquire the only slot
        using var slot1 = await limiter.AcquireEmbeddingSlotAsync();

        // Act & Assert
        cts.CancelAfter(50);
        await Assert.ThrowsAsync<OperationCanceledException>(
            () => limiter.AcquireEmbeddingSlotAsync(cts.Token));
    }

    [Fact]
    public async Task AcquireFileReadSlotAsync_ShouldAcquireAndRelease()
    {
        // Arrange
        var limiter = CreateLimiter(fileReadConcurrency: 4);

        // Act
        var slot = await limiter.AcquireFileReadSlotAsync();
        
        // Assert - Should not throw
        Assert.NotNull(slot);
        slot.Dispose();
    }

    [Fact]
    public async Task ParallelEmbeddingAcquisitions_ShouldRespectLimit()
    {
        // Arrange
        var limiter = CreateLimiter(embeddingConcurrency: 3);
        var acquiredCount = 0;
        var maxConcurrent = 0;
        var lockObj = new object();

        // Act
        var tasks = Enumerable.Range(0, 10).Select(async _ =>
        {
            using var slot = await limiter.AcquireEmbeddingSlotAsync();
            
            lock (lockObj)
            {
                acquiredCount++;
                maxConcurrent = Math.Max(maxConcurrent, acquiredCount);
            }
            
            await Task.Delay(10);
            
            lock (lockObj)
            {
                acquiredCount--;
            }
        });

        await Task.WhenAll(tasks);

        // Assert
        Assert.True(maxConcurrent <= 3, $"Max concurrent was {maxConcurrent}, expected <= 3");
    }

    [Fact]
    public async Task ParallelReasoningAcquisitions_ShouldRespectLimit()
    {
        // Arrange
        var limiter = CreateLimiter(reasoningConcurrency: 2);
        var acquiredCount = 0;
        var maxConcurrent = 0;
        var lockObj = new object();

        // Act
        var tasks = Enumerable.Range(0, 8).Select(async _ =>
        {
            using var slot = await limiter.AcquireReasoningSlotAsync();
            
            lock (lockObj)
            {
                acquiredCount++;
                maxConcurrent = Math.Max(maxConcurrent, acquiredCount);
            }
            
            await Task.Delay(10);
            
            lock (lockObj)
            {
                acquiredCount--;
            }
        });

        await Task.WhenAll(tasks);

        // Assert
        Assert.True(maxConcurrent <= 2, $"Max concurrent was {maxConcurrent}, expected <= 2");
    }

    [Fact]
    public void Dispose_ShouldNotThrow()
    {
        // Arrange
        var limiter = CreateLimiter();

        // Act & Assert
        var exception = Record.Exception(() => limiter.Dispose());
        Assert.Null(exception);
    }

    [Fact]
    public void Dispose_Twice_ShouldNotThrow()
    {
        // Arrange
        var limiter = CreateLimiter();

        // Act & Assert
        limiter.Dispose();
        var exception = Record.Exception(() => limiter.Dispose());
        Assert.Null(exception);
    }
}
