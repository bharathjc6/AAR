// =============================================================================
// AAR.Tests - Infrastructure/EmbeddingServiceTests.cs
// Unit tests for embedding service implementations
// =============================================================================

using AAR.Application.Interfaces;
using AAR.Infrastructure.Services.Embedding;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace AAR.Tests.Infrastructure;

public class EmbeddingServiceTests
{
    [Fact]
    public async Task MockEmbeddingService_CreateEmbeddingAsync_ReturnsDeterministicVector()
    {
        // Arrange
        var options = Options.Create(new EmbeddingOptions { Dimension = 1536 });
        var service = new MockEmbeddingService(options, Mock.Of<ILogger<MockEmbeddingService>>());
        var text = "Hello, World!";

        // Act
        var embedding1 = await service.CreateEmbeddingAsync(text);
        var embedding2 = await service.CreateEmbeddingAsync(text);

        // Assert
        embedding1.Should().HaveCount(1536);
        embedding1.Should().BeEquivalentTo(embedding2, "same text should produce same embedding");
    }

    [Fact]
    public async Task MockEmbeddingService_CreateEmbeddingAsync_DifferentTextsDifferentVectors()
    {
        // Arrange
        var options = Options.Create(new EmbeddingOptions { Dimension = 1536 });
        var service = new MockEmbeddingService(options, Mock.Of<ILogger<MockEmbeddingService>>());

        // Act
        var embedding1 = await service.CreateEmbeddingAsync("Hello");
        var embedding2 = await service.CreateEmbeddingAsync("Goodbye");

        // Assert
        embedding1.Should().NotBeEquivalentTo(embedding2);
    }

    [Fact]
    public async Task MockEmbeddingService_CreateEmbeddingAsync_ProducesNormalizedVector()
    {
        // Arrange
        var options = Options.Create(new EmbeddingOptions { Dimension = 1536 });
        var service = new MockEmbeddingService(options, Mock.Of<ILogger<MockEmbeddingService>>());

        // Act
        var embedding = await service.CreateEmbeddingAsync("Test text");

        // Assert
        // Check that vector is normalized (magnitude ≈ 1)
        var magnitude = Math.Sqrt(embedding.Sum(x => x * x));
        magnitude.Should().BeApproximately(1.0, 0.01);
    }

    [Fact]
    public async Task MockEmbeddingService_CreateEmbeddingsAsync_ReturnsSameCountAsInput()
    {
        // Arrange
        var options = Options.Create(new EmbeddingOptions { Dimension = 1536 });
        var service = new MockEmbeddingService(options, Mock.Of<ILogger<MockEmbeddingService>>());
        var texts = new List<string> { "Text 1", "Text 2", "Text 3" };

        // Act
        var embeddings = await service.CreateEmbeddingsAsync(texts);

        // Assert
        embeddings.Should().HaveCount(3);
    }

    [Fact]
    public async Task MockEmbeddingService_CreateEmbeddingsAsync_EachEmbeddingIsDeterministic()
    {
        // Arrange
        var options = Options.Create(new EmbeddingOptions { Dimension = 1536 });
        var service = new MockEmbeddingService(options, Mock.Of<ILogger<MockEmbeddingService>>());
        var texts = new List<string> { "Alpha", "Beta", "Gamma" };

        // Act
        var batch1 = await service.CreateEmbeddingsAsync(texts);
        var batch2 = await service.CreateEmbeddingsAsync(texts);

        // Assert
        for (int i = 0; i < texts.Count; i++)
        {
            batch1[i].Should().BeEquivalentTo(batch2[i]);
        }
    }

    [Fact]
    public async Task MockEmbeddingService_CreateEmbeddingsAsync_WithEmptyList_ReturnsEmptyList()
    {
        // Arrange
        var options = Options.Create(new EmbeddingOptions { Dimension = 1536 });
        var service = new MockEmbeddingService(options, Mock.Of<ILogger<MockEmbeddingService>>());

        // Act
        var embeddings = await service.CreateEmbeddingsAsync(new List<string>());

        // Assert
        embeddings.Should().BeEmpty();
    }

    [Fact]
    public async Task MockEmbeddingService_CreateEmbeddingAsync_WithEmptyText_ReturnsZeroVector()
    {
        // Arrange
        var options = Options.Create(new EmbeddingOptions { Dimension = 1536 });
        var service = new MockEmbeddingService(options, Mock.Of<ILogger<MockEmbeddingService>>());

        // Act
        var embedding = await service.CreateEmbeddingAsync("");

        // Assert
        embedding.Should().HaveCount(1536);
        // Empty text produces zero vector
        embedding.Should().OnlyContain(x => x == 0);
    }

    [Theory]
    [InlineData(256)]
    [InlineData(512)]
    [InlineData(1536)]
    [InlineData(3072)]
    public async Task MockEmbeddingService_CreateEmbeddingAsync_RespectsConfiguredDimensions(int dimensions)
    {
        // Arrange
        var options = Options.Create(new EmbeddingOptions { Dimension = dimensions });
        var service = new MockEmbeddingService(options, Mock.Of<ILogger<MockEmbeddingService>>());

        // Act
        var embedding = await service.CreateEmbeddingAsync("Test");

        // Assert
        embedding.Should().HaveCount(dimensions);
    }

    [Fact]
    public void EmbeddingOptions_DefaultValues_AreCorrect()
    {
        // Arrange & Act
        var options = new EmbeddingOptions();

        // Assert
        options.Dimension.Should().Be(1536);
        options.BatchSize.Should().Be(16);
        options.Model.Should().Be("text-embedding-ada-002");
    }

    [Fact]
    public void MockEmbeddingService_IsMock_ReturnsTrue()
    {
        // Arrange
        var options = Options.Create(new EmbeddingOptions { Dimension = 1536 });
        var service = new MockEmbeddingService(options, Mock.Of<ILogger<MockEmbeddingService>>());

        // Assert
        service.IsMock.Should().BeTrue();
        service.ModelName.Should().Be("mock-embedding");
    }
}
