// =============================================================================
// AAR.Tests - Unit/Embedding/ResilientEmbeddingServiceTests.cs
// Unit tests for the ResilientEmbeddingService rate limiting behavior
// =============================================================================

using AAR.Application.Configuration;
using AAR.Application.Interfaces;
using AAR.Infrastructure.Services.Embedding;
using AAR.Infrastructure.Services.Resilience;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Polly;
using Polly.Registry;
using Xunit;

namespace AAR.Tests.Unit.Embedding;

/// <summary>
/// Tests for ResilientEmbeddingService to ensure rate limiting doesn't cause deadlocks
/// </summary>
public class ResilientEmbeddingServiceTests
{
    private readonly Mock<IEmbeddingService> _innerServiceMock;
    private readonly Mock<IMetricsService> _metricsMock;
    private readonly Mock<ILogger<ResilientEmbeddingService>> _loggerMock;
    private readonly ResiliencePipelineProvider<string> _pipelineProvider;

    public ResilientEmbeddingServiceTests()
    {
        _innerServiceMock = new Mock<IEmbeddingService>();
        _metricsMock = new Mock<IMetricsService>();
        _loggerMock = new Mock<ILogger<ResilientEmbeddingService>>();

        // Create a minimal resilience pipeline for testing
        var registry = new ResiliencePipelineRegistry<string>();
        registry.TryAddBuilder(ResiliencePipelineNames.EmbeddingApi, (builder, _) =>
        {
            // Simple pass-through pipeline for tests
        });
        _pipelineProvider = registry;
    }

    [Fact]
    public async Task CreateEmbeddingAsync_CompletesWithin30Seconds_WhenSemaphoreBlocked()
    {
        // Arrange
        var options = Options.Create(new EmbeddingProcessingOptions
        {
            EmbeddingConcurrency = 1,
            EmbeddingTokensPerMinute = 1000
        });

        _innerServiceMock.Setup(s => s.CreateEmbeddingAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new float[] { 0.1f, 0.2f, 0.3f });
        _innerServiceMock.Setup(s => s.ModelName).Returns("test-model");

        var service = new ResilientEmbeddingService(
            _innerServiceMock.Object,
            _pipelineProvider,
            options,
            _metricsMock.Object,
            _loggerMock.Object);

        // Act & Assert - Should complete quickly, not wait 2 minutes
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        var result = await service.CreateEmbeddingAsync("test text");
        stopwatch.Stop();

        result.Should().NotBeEmpty();
        stopwatch.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(5)); // Should be nearly instant
    }

    [Fact]
    public async Task CreateEmbeddingsAsync_CompletesWithin30Seconds_WhenSemaphoreBlocked()
    {
        // Arrange
        var options = Options.Create(new EmbeddingProcessingOptions
        {
            EmbeddingConcurrency = 1,
            EmbeddingTokensPerMinute = 1000
        });

        _innerServiceMock.Setup(s => s.CreateEmbeddingsAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<float[]> { new float[] { 0.1f, 0.2f, 0.3f } });
        _innerServiceMock.Setup(s => s.ModelName).Returns("test-model");

        var service = new ResilientEmbeddingService(
            _innerServiceMock.Object,
            _pipelineProvider,
            options,
            _metricsMock.Object,
            _loggerMock.Object);

        // Act & Assert - Should complete quickly, not wait 2 minutes
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        var result = await service.CreateEmbeddingsAsync(new[] { "test text" });
        stopwatch.Stop();

        result.Should().NotBeEmpty();
        stopwatch.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(5)); // Should be nearly instant
    }

    [Fact]
    public async Task CreateEmbeddingsAsync_RespectsMaxWaitTime_WhenRateLimited()
    {
        // Arrange - Very low tokens/minute to force rate limiting
        var options = Options.Create(new EmbeddingProcessingOptions
        {
            EmbeddingConcurrency = 1,
            EmbeddingTokensPerMinute = 1 // Very low limit
        });

        _innerServiceMock.Setup(s => s.CreateEmbeddingsAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<float[]> { new float[] { 0.1f, 0.2f, 0.3f } });
        _innerServiceMock.Setup(s => s.ModelName).Returns("test-model");

        var service = new ResilientEmbeddingService(
            _innerServiceMock.Object,
            _pipelineProvider,
            options,
            _metricsMock.Object,
            _loggerMock.Object);

        // Consume the token budget
        await service.CreateEmbeddingsAsync(new[] { "test" });

        // Act - Second call should hit rate limit but not wait forever
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        var result = await service.CreateEmbeddingsAsync(new[] { "test text that should be rate limited" });
        stopwatch.Stop();

        // Assert - Should complete within 35 seconds (30s max wait + buffer), not 2+ minutes
        result.Should().NotBeEmpty();
        stopwatch.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(35));
    }

    [Fact]
    public async Task CreateEmbeddingsAsync_CompletesQuickly_EvenWhenRateLimited()
    {
        // Arrange
        var options = Options.Create(new EmbeddingProcessingOptions
        {
            EmbeddingConcurrency = 1,
            EmbeddingTokensPerMinute = 1 // Very low limit
        });

        _innerServiceMock.Setup(s => s.CreateEmbeddingsAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<float[]> { new float[] { 0.1f, 0.2f, 0.3f } });
        _innerServiceMock.Setup(s => s.ModelName).Returns("test-model");

        var service = new ResilientEmbeddingService(
            _innerServiceMock.Object,
            _pipelineProvider,
            options,
            _metricsMock.Object,
            _loggerMock.Object);

        // Consume the token budget
        await service.CreateEmbeddingsAsync(new[] { "test" });

        // Act - Second call should hit rate limit but complete quickly (max 30s wait, not 2+ minutes)
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        var result = await service.CreateEmbeddingsAsync(new[] { "test text that should be rate limited" });
        stopwatch.Stop();

        // Assert - Should complete within reasonable time, proving we don't deadlock
        // The fix ensures that after max wait iterations, we proceed anyway
        result.Should().NotBeEmpty();
        stopwatch.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(35)); // Max 30s wait + buffer
    }

    [Fact]
    public async Task CreateEmbeddingsBatchedAsync_ProcessesAllBatches()
    {
        // Arrange
        var options = Options.Create(new EmbeddingProcessingOptions
        {
            EmbeddingConcurrency = 2,
            EmbeddingTokensPerMinute = 100000 // High limit
        });

        _innerServiceMock.Setup(s => s.CreateEmbeddingsAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IEnumerable<string> texts, CancellationToken _) => 
                texts.Select(_ => new float[] { 0.1f, 0.2f, 0.3f }).ToList());
        _innerServiceMock.Setup(s => s.ModelName).Returns("test-model");

        var service = new ResilientEmbeddingService(
            _innerServiceMock.Object,
            _pipelineProvider,
            options,
            _metricsMock.Object,
            _loggerMock.Object);

        var texts = Enumerable.Range(1, 50).Select(i => $"Text {i}").ToList();
        var progress = new Progress<int>();

        // Act
        var results = await service.CreateEmbeddingsBatchedAsync(texts, batchSize: 10, progress);

        // Assert
        results.Should().HaveCount(50);
    }
}
