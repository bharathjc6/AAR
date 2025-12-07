// =============================================================================
// AAR.Tests - Infrastructure/VectorStoreTests.cs
// Unit tests for vector store implementations
// =============================================================================

using AAR.Application.Interfaces;
using AAR.Infrastructure.Services.VectorStore;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace AAR.Tests.Infrastructure;

public class VectorStoreTests
{
    private readonly InMemoryVectorStore _vectorStore;

    public VectorStoreTests()
    {
        _vectorStore = new InMemoryVectorStore(Mock.Of<ILogger<InMemoryVectorStore>>());
    }

    [Fact]
    public async Task IndexVectorAsync_StoresVector()
    {
        // Arrange
        var chunkId = "chunk1";
        var vector = new float[] { 1.0f, 0.0f, 0.0f };
        var metadata = CreateMetadata(Guid.NewGuid(), "test.cs");

        // Act
        await _vectorStore.IndexVectorAsync(chunkId, vector, metadata);

        // Assert
        var count = await _vectorStore.CountAsync();
        count.Should().Be(1);
    }

    [Fact]
    public async Task IndexVectorsAsync_StoresBatch()
    {
        // Arrange
        var projectId = Guid.NewGuid();
        var vectors = new List<(string, float[], VectorMetadata)>
        {
            ("chunk1", new float[] { 1.0f, 0.0f, 0.0f }, CreateMetadata(projectId, "file1.cs")),
            ("chunk2", new float[] { 0.0f, 1.0f, 0.0f }, CreateMetadata(projectId, "file2.cs"))
        };

        // Act
        await _vectorStore.IndexVectorsAsync(vectors);

        // Assert
        var count = await _vectorStore.CountAsync();
        count.Should().Be(2);
    }

    [Fact]
    public async Task QueryAsync_ReturnsRelevantResults()
    {
        // Arrange
        var projectId = Guid.NewGuid();
        await _vectorStore.IndexVectorsAsync(new[]
        {
            ("auth-chunk", new float[] { 1.0f, 0.0f, 0.0f }, CreateMetadata(projectId, "auth.cs")),
            ("data-chunk", new float[] { 0.0f, 1.0f, 0.0f }, CreateMetadata(projectId, "data.cs")),
            ("ui-chunk", new float[] { 0.0f, 0.0f, 1.0f }, CreateMetadata(projectId, "ui.cs"))
        });

        // Query vector similar to auth-chunk
        var queryVector = new float[] { 0.9f, 0.1f, 0.0f };

        // Act
        var results = await _vectorStore.QueryAsync(queryVector, topK: 1);

        // Assert
        results.Should().HaveCount(1);
        results[0].ChunkId.Should().Be("auth-chunk");
    }

    [Fact]
    public async Task QueryAsync_ReturnsTopKResults()
    {
        // Arrange
        var projectId = Guid.NewGuid();
        await _vectorStore.IndexVectorsAsync(new[]
        {
            ("chunk1", new float[] { 1.0f, 0.0f, 0.0f }, CreateMetadata(projectId, "f1.cs")),
            ("chunk2", new float[] { 0.9f, 0.1f, 0.0f }, CreateMetadata(projectId, "f2.cs")),
            ("chunk3", new float[] { 0.8f, 0.2f, 0.0f }, CreateMetadata(projectId, "f3.cs")),
            ("chunk4", new float[] { 0.7f, 0.3f, 0.0f }, CreateMetadata(projectId, "f4.cs")),
            ("chunk5", new float[] { 0.6f, 0.4f, 0.0f }, CreateMetadata(projectId, "f5.cs"))
        });

        var queryVector = new float[] { 1.0f, 0.0f, 0.0f };

        // Act
        var results = await _vectorStore.QueryAsync(queryVector, topK: 3);

        // Assert
        results.Should().HaveCount(3);
    }

    [Fact]
    public async Task QueryAsync_OrdersBySimilarityDescending()
    {
        // Arrange
        var projectId = Guid.NewGuid();
        await _vectorStore.IndexVectorsAsync(new[]
        {
            ("low", new float[] { 0.5f, 0.5f, 0.0f }, CreateMetadata(projectId, "low.cs")),
            ("high", new float[] { 1.0f, 0.0f, 0.0f }, CreateMetadata(projectId, "high.cs")),
            ("medium", new float[] { 0.7f, 0.3f, 0.0f }, CreateMetadata(projectId, "med.cs"))
        });

        var queryVector = new float[] { 1.0f, 0.0f, 0.0f };

        // Act
        var results = await _vectorStore.QueryAsync(queryVector, topK: 3);

        // Assert
        results[0].ChunkId.Should().Be("high");
        results[0].Score.Should().BeGreaterThan(results[1].Score);
        results[1].Score.Should().BeGreaterThan(results[2].Score);
    }

    [Fact]
    public async Task QueryAsync_FiltersByProjectId()
    {
        // Arrange
        var project1 = Guid.NewGuid();
        var project2 = Guid.NewGuid();
        
        await _vectorStore.IndexVectorsAsync(new[]
        {
            ("p1-chunk", new float[] { 1.0f, 0.0f, 0.0f }, CreateMetadata(project1, "p1.cs")),
            ("p2-chunk", new float[] { 1.0f, 0.0f, 0.0f }, CreateMetadata(project2, "p2.cs"))
        });

        var queryVector = new float[] { 1.0f, 0.0f, 0.0f };

        // Act
        var results = await _vectorStore.QueryAsync(queryVector, topK: 10, projectId: project1);

        // Assert
        results.Should().HaveCount(1);
        results[0].ChunkId.Should().Be("p1-chunk");
    }

    [Fact]
    public async Task DeleteByProjectIdAsync_RemovesProjectVectors()
    {
        // Arrange
        var project1 = Guid.NewGuid();
        var project2 = Guid.NewGuid();
        
        await _vectorStore.IndexVectorsAsync(new[]
        {
            ("p1-chunk", new float[] { 1.0f, 0.0f, 0.0f }, CreateMetadata(project1, "p1.cs")),
            ("p2-chunk", new float[] { 0.0f, 1.0f, 0.0f }, CreateMetadata(project2, "p2.cs"))
        });

        // Act
        await _vectorStore.DeleteByProjectIdAsync(project1);

        // Assert
        var count = await _vectorStore.CountAsync();
        count.Should().Be(1);
        
        var results = await _vectorStore.QueryAsync(new float[] { 1.0f, 0.0f, 0.0f }, topK: 10);
        results.Should().HaveCount(1);
        results[0].ChunkId.Should().Be("p2-chunk");
    }

    [Fact]
    public async Task DeleteAsync_RemovesSpecificVector()
    {
        // Arrange
        var projectId = Guid.NewGuid();
        await _vectorStore.IndexVectorsAsync(new[]
        {
            ("chunk1", new float[] { 1.0f, 0.0f, 0.0f }, CreateMetadata(projectId, "f1.cs")),
            ("chunk2", new float[] { 0.0f, 1.0f, 0.0f }, CreateMetadata(projectId, "f2.cs"))
        });

        // Act
        await _vectorStore.DeleteAsync("chunk1");

        // Assert
        var count = await _vectorStore.CountAsync();
        count.Should().Be(1);
    }

    [Fact]
    public async Task CountAsync_ReturnsCorrectCount()
    {
        // Arrange
        var projectId = Guid.NewGuid();
        await _vectorStore.IndexVectorsAsync(new[]
        {
            ("chunk1", new float[] { 1, 0, 0 }, CreateMetadata(projectId, "f1.cs")),
            ("chunk2", new float[] { 0, 1, 0 }, CreateMetadata(projectId, "f2.cs")),
            ("chunk3", new float[] { 0, 0, 1 }, CreateMetadata(projectId, "f3.cs"))
        });

        // Act
        var count = await _vectorStore.CountAsync();

        // Assert
        count.Should().Be(3);
    }

    [Fact]
    public async Task CountAsync_WithProjectFilter_CountsProjectOnly()
    {
        // Arrange
        var project1 = Guid.NewGuid();
        var project2 = Guid.NewGuid();
        
        await _vectorStore.IndexVectorsAsync(new[]
        {
            ("p1-a", new float[] { 1, 0, 0 }, CreateMetadata(project1, "a.cs")),
            ("p1-b", new float[] { 0, 1, 0 }, CreateMetadata(project1, "b.cs")),
            ("p2-a", new float[] { 0, 0, 1 }, CreateMetadata(project2, "c.cs"))
        });

        // Act
        var count = await _vectorStore.CountAsync(project1);

        // Assert
        count.Should().Be(2);
    }

    [Fact]
    public async Task IndexVectorAsync_UpdatesExistingVector()
    {
        // Arrange
        var projectId = Guid.NewGuid();
        var chunkId = "chunk1";
        
        await _vectorStore.IndexVectorAsync(chunkId, new float[] { 1, 0, 0 }, 
            CreateMetadata(projectId, "original.cs"));

        // Act
        await _vectorStore.IndexVectorAsync(chunkId, new float[] { 0, 1, 0 }, 
            CreateMetadata(projectId, "updated.cs"));

        // Assert
        var count = await _vectorStore.CountAsync();
        count.Should().Be(1); // Still only one vector
        
        // Query should find the updated vector
        var results = await _vectorStore.QueryAsync(new float[] { 0, 1, 0 }, topK: 1);
        results[0].Metadata.FilePath.Should().Be("updated.cs");
    }

    private static VectorMetadata CreateMetadata(Guid projectId, string filePath)
    {
        return new VectorMetadata
        {
            ProjectId = projectId,
            FilePath = filePath,
            StartLine = 1,
            EndLine = 10
        };
    }
}
