// =============================================================================
// AAR.Tests - Infrastructure/RetrievalOrchestratorTests.cs
// Tests for RetrievalOrchestrator batch processing and memory management
// =============================================================================

using AAR.Application.Interfaces;
using AAR.Domain.Entities;
using AAR.Domain.Interfaces;
using AAR.Infrastructure.Services.Retrieval;
using AAR.Shared.Tokenization;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace AAR.Tests.Infrastructure;

public class RetrievalOrchestratorTests
{
    private readonly Mock<IChunker> _chunkerMock;
    private readonly Mock<IEmbeddingService> _embeddingServiceMock;
    private readonly Mock<IVectorStore> _vectorStoreMock;
    private readonly Mock<IOpenAiService> _openAiServiceMock;
    private readonly Mock<IChunkRepository> _chunkRepositoryMock;
    private readonly Mock<IUnitOfWork> _unitOfWorkMock;
    private readonly Mock<ITokenizerFactory> _tokenizerFactoryMock;
    private readonly Mock<ITokenizer> _tokenizerMock;
    private readonly Mock<IJobProgressService> _progressServiceMock;
    private readonly IOptions<ChunkerOptions> _chunkerOptions;
    private readonly IOptions<ModelRouterOptions> _routerOptions;
    private readonly Mock<ILogger<RetrievalOrchestrator>> _loggerMock;
    private readonly RetrievalOrchestrator _orchestrator;

    public RetrievalOrchestratorTests()
    {
        _chunkerMock = new Mock<IChunker>();
        _embeddingServiceMock = new Mock<IEmbeddingService>();
        _vectorStoreMock = new Mock<IVectorStore>();
        _openAiServiceMock = new Mock<IOpenAiService>();
        _chunkRepositoryMock = new Mock<IChunkRepository>();
        _unitOfWorkMock = new Mock<IUnitOfWork>();
        _tokenizerFactoryMock = new Mock<ITokenizerFactory>();
        _tokenizerMock = new Mock<ITokenizer>();
        _progressServiceMock = new Mock<IJobProgressService>();
        _loggerMock = new Mock<ILogger<RetrievalOrchestrator>>();

        _tokenizerFactoryMock.Setup(f => f.Create()).Returns(_tokenizerMock.Object);
        _embeddingServiceMock.Setup(e => e.ModelName).Returns("test-model");

        _chunkerOptions = Options.Create(new ChunkerOptions
        {
            MaxChunkTokens = 1600,
            MinChunkTokens = 100,
            OverlapTokens = 200,
            StoreChunkText = false
        });

        _routerOptions = Options.Create(new ModelRouterOptions
        {
            TopK = 20,
            SummarizationThreshold = 8000
        });

        _orchestrator = new RetrievalOrchestrator(
            _chunkerMock.Object,
            _embeddingServiceMock.Object,
            _vectorStoreMock.Object,
            _openAiServiceMock.Object,
            _chunkRepositoryMock.Object,
            _unitOfWorkMock.Object,
            _tokenizerFactoryMock.Object,
            _progressServiceMock.Object,
            _chunkerOptions,
            _routerOptions,
            _loggerMock.Object);
    }

    [Fact]
    public async Task IndexProjectAsync_WithLargeProject_ProcessesAllBatches()
    {
        // Arrange
        var projectId = Guid.NewGuid();
        var files = GenerateLargeFileSet(100); // 100 files

        var chunkInfos = files.Keys.Select(path => new ChunkInfo
        {
            ChunkHash = Guid.NewGuid().ToString(),
            ProjectId = projectId,
            FilePath = path,
            StartLine = 1,
            EndLine = 100,
            TokenCount = 500,
            Language = "csharp",
            TextHash = Guid.NewGuid().ToString(),
            Content = "test content"
        }).ToList();

        _chunkerMock
            .Setup(c => c.ChunkFilesAsync(It.IsAny<IDictionary<string, string>>(), projectId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((IDictionary<string, string> f, Guid p, CancellationToken ct) =>
                chunkInfos.Where(c => f.ContainsKey(c.FilePath)).ToList());

        _embeddingServiceMock
            .Setup(e => e.CreateEmbeddingsAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IEnumerable<string> texts, CancellationToken ct) =>
                texts.Select(_ => new float[1536]).ToList() as IReadOnlyList<float[]>);

        _unitOfWorkMock.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
        _unitOfWorkMock.Setup(u => u.ClearChangeTracker());

        // Act
        var result = await _orchestrator.IndexProjectAsync(projectId, files);

        // Assert
        result.FilesProcessed.Should().Be(100);
        result.ChunksCreated.Should().Be(100);
        result.EmbeddingsGenerated.Should().Be(100);
        result.Errors.Should().BeNullOrEmpty();

        // Verify ClearChangeTracker was called multiple times (once per chunk batch)
        _unitOfWorkMock.Verify(u => u.ClearChangeTracker(), Times.AtLeast(2));
    }

    [Fact]
    public async Task IndexProjectAsync_WithVeryLargeProject_HandlesMemoryEfficiently()
    {
        // Arrange
        var projectId = Guid.NewGuid();
        var files = GenerateLargeFileSet(500); // 500 files - should trigger multiple batches

        var chunkInfos = new List<ChunkInfo>();
        foreach (var file in files.Keys)
        {
            // Each file produces 3 chunks
            for (int i = 0; i < 3; i++)
            {
                chunkInfos.Add(new ChunkInfo
                {
                    ChunkHash = Guid.NewGuid().ToString(),
                    ProjectId = projectId,
                    FilePath = file,
                    StartLine = i * 50 + 1,
                    EndLine = (i + 1) * 50,
                    TokenCount = 400,
                    Language = "csharp",
                    TextHash = Guid.NewGuid().ToString(),
                    Content = new string('x', 1000) // 1KB content each
                });
            }
        }

        _chunkerMock
            .Setup(c => c.ChunkFilesAsync(It.IsAny<IDictionary<string, string>>(), projectId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((IDictionary<string, string> f, Guid p, CancellationToken ct) =>
                chunkInfos.Where(c => f.ContainsKey(c.FilePath)).ToList());

        _embeddingServiceMock
            .Setup(e => e.CreateEmbeddingsAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IEnumerable<string> texts, CancellationToken ct) =>
                texts.Select(_ => new float[1536]).ToList() as IReadOnlyList<float[]>);

        _unitOfWorkMock.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
        _unitOfWorkMock.Setup(u => u.ClearChangeTracker());

        var memoryBefore = GC.GetTotalMemory(true);

        // Act
        var result = await _orchestrator.IndexProjectAsync(projectId, files);

        var memoryAfter = GC.GetTotalMemory(true);
        var memoryUsedMB = (memoryAfter - memoryBefore) / 1024.0 / 1024.0;

        // Assert
        result.FilesProcessed.Should().Be(500);
        result.ChunksCreated.Should().Be(1500); // 500 files x 3 chunks each
        result.Errors.Should().BeNullOrEmpty();

        // Memory usage should be reasonable (less than 500MB for this test)
        // This is a loose check - the actual memory usage depends on many factors
        memoryUsedMB.Should().BeLessThan(500, "Memory usage should be controlled with batch processing");

        // Verify multiple batch saves
        _unitOfWorkMock.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.AtLeast(10));
    }

    [Fact]
    public async Task IndexProjectAsync_CancellationToken_StopsProcessing()
    {
        // Arrange
        var projectId = Guid.NewGuid();
        var files = GenerateLargeFileSet(100);
        var cts = new CancellationTokenSource();
        var batchCount = 0;

        _chunkerMock
            .Setup(c => c.ChunkFilesAsync(It.IsAny<IDictionary<string, string>>(), projectId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((IDictionary<string, string> f, Guid p, CancellationToken ct) =>
            {
                batchCount++;
                if (batchCount >= 2)
                {
                    cts.Cancel(); // Cancel after second batch
                }
                return f.Keys.Select(path => new ChunkInfo
                {
                    ChunkHash = Guid.NewGuid().ToString(),
                    ProjectId = projectId,
                    FilePath = path,
                    StartLine = 1,
                    EndLine = 100,
                    TokenCount = 500,
                    Language = "csharp",
                    TextHash = Guid.NewGuid().ToString(),
                    Content = "test"
                }).ToList();
            });

        _embeddingServiceMock
            .Setup(e => e.CreateEmbeddingsAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IEnumerable<string> texts, CancellationToken ct) =>
                texts.Select(_ => new float[1536]).ToList() as IReadOnlyList<float[]>);

        _unitOfWorkMock.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
        _unitOfWorkMock.Setup(u => u.ClearChangeTracker());

        // Act & Assert
        await Assert.ThrowsAsync<OperationCanceledException>(
            () => _orchestrator.IndexProjectAsync(projectId, files, cts.Token));
    }

    [Fact]
    public async Task IndexProjectAsync_WithEmptyFiles_ReturnsZeroChunks()
    {
        // Arrange
        var projectId = Guid.NewGuid();
        var files = new Dictionary<string, string>();

        _unitOfWorkMock.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
        _unitOfWorkMock.Setup(u => u.ClearChangeTracker());

        // Act
        var result = await _orchestrator.IndexProjectAsync(projectId, files);

        // Assert
        result.FilesProcessed.Should().Be(0);
        result.ChunksCreated.Should().Be(0);
    }

    [Fact]
    public async Task IndexProjectAsync_WithErrorInBatch_ContinuesProcessing()
    {
        // Arrange
        var projectId = Guid.NewGuid();
        var files = GenerateLargeFileSet(100);
        var batchCount = 0;

        _chunkerMock
            .Setup(c => c.ChunkFilesAsync(It.IsAny<IDictionary<string, string>>(), projectId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((IDictionary<string, string> f, Guid p, CancellationToken ct) =>
            {
                batchCount++;
                if (batchCount == 2)
                {
                    throw new InvalidOperationException("Test error");
                }
                return f.Keys.Select(path => new ChunkInfo
                {
                    ChunkHash = Guid.NewGuid().ToString(),
                    ProjectId = projectId,
                    FilePath = path,
                    StartLine = 1,
                    EndLine = 100,
                    TokenCount = 500,
                    Language = "csharp",
                    TextHash = Guid.NewGuid().ToString(),
                    Content = "test"
                }).ToList();
            });

        _embeddingServiceMock
            .Setup(e => e.CreateEmbeddingsAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IEnumerable<string> texts, CancellationToken ct) =>
                texts.Select(_ => new float[1536]).ToList() as IReadOnlyList<float[]>);

        _unitOfWorkMock.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
        _unitOfWorkMock.Setup(u => u.ClearChangeTracker());

        // Act
        var result = await _orchestrator.IndexProjectAsync(projectId, files);

        // Assert
        result.FilesProcessed.Should().Be(100); // All files should be counted
        result.Errors.Should().NotBeNullOrEmpty();
        result.Errors.Should().Contain(e => e.Contains("Batch 2"));
    }

    [Fact]
    public async Task IndexProjectStreamingAsync_WithRealFiles_ProcessesSuccessfully()
    {
        // Arrange
        var projectId = Guid.NewGuid();
        var tempDir = Path.Combine(Path.GetTempPath(), $"aar-test-{Guid.NewGuid():N}");
        
        try
        {
            // Create temp directory with test files
            Directory.CreateDirectory(tempDir);
            Directory.CreateDirectory(Path.Combine(tempDir, "src"));
            
            for (var i = 0; i < 20; i++)
            {
                var content = $@"
namespace TestNamespace
{{
    public class TestClass{i}
    {{
        public void Method1() {{ }}
        public void Method2() {{ }}
    }}
}}";
                await File.WriteAllTextAsync(
                    Path.Combine(tempDir, "src", $"File{i}.cs"), 
                    content);
            }

            _chunkerMock
                .Setup(c => c.ChunkFilesAsync(It.IsAny<IDictionary<string, string>>(), projectId, It.IsAny<CancellationToken>()))
                .ReturnsAsync((IDictionary<string, string> f, Guid p, CancellationToken ct) =>
                    f.Keys.Select(path => new ChunkInfo
                    {
                        ChunkHash = Guid.NewGuid().ToString(),
                        ProjectId = p,
                        FilePath = path,
                        StartLine = 1,
                        EndLine = 10,
                        TokenCount = 100,
                        Language = "csharp",
                        TextHash = Guid.NewGuid().ToString(),
                        Content = "test content"
                    }).ToList());

            _embeddingServiceMock
                .Setup(e => e.CreateEmbeddingsAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((IEnumerable<string> texts, CancellationToken ct) =>
                    texts.Select(_ => new float[1536]).ToList() as IReadOnlyList<float[]>);

            _unitOfWorkMock.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
            _unitOfWorkMock.Setup(u => u.ClearChangeTracker());

            // Act
            var result = await _orchestrator.IndexProjectStreamingAsync(projectId, tempDir);

            // Assert
            result.FilesProcessed.Should().Be(20);
            result.ChunksCreated.Should().Be(20);
            result.Errors.Should().BeNullOrEmpty();
            
            // Verify change tracker was cleared multiple times
            _unitOfWorkMock.Verify(u => u.ClearChangeTracker(), Times.AtLeast(2));
        }
        finally
        {
            // Cleanup
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, true);
            }
        }
    }

    [Fact]
    public async Task IndexProjectStreamingAsync_ExcludesNodeModules()
    {
        // Arrange
        var projectId = Guid.NewGuid();
        var tempDir = Path.Combine(Path.GetTempPath(), $"aar-test-{Guid.NewGuid():N}");
        
        try
        {
            // Create temp directory with test files including node_modules
            Directory.CreateDirectory(tempDir);
            Directory.CreateDirectory(Path.Combine(tempDir, "src"));
            Directory.CreateDirectory(Path.Combine(tempDir, "node_modules", "some-package"));
            
            await File.WriteAllTextAsync(Path.Combine(tempDir, "src", "main.cs"), "// main code");
            await File.WriteAllTextAsync(Path.Combine(tempDir, "node_modules", "some-package", "index.js"), "// package code");

            var processedFiles = new List<string>();
            
            _chunkerMock
                .Setup(c => c.ChunkFilesAsync(It.IsAny<IDictionary<string, string>>(), projectId, It.IsAny<CancellationToken>()))
                .ReturnsAsync((IDictionary<string, string> f, Guid p, CancellationToken ct) =>
                {
                    processedFiles.AddRange(f.Keys);
                    return f.Keys.Select(path => new ChunkInfo
                    {
                        ChunkHash = Guid.NewGuid().ToString(),
                        ProjectId = p,
                        FilePath = path,
                        StartLine = 1,
                        EndLine = 1,
                        TokenCount = 10,
                        Language = "csharp",
                        TextHash = Guid.NewGuid().ToString(),
                        Content = "test"
                    }).ToList();
                });

            _embeddingServiceMock
                .Setup(e => e.CreateEmbeddingsAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((IEnumerable<string> texts, CancellationToken ct) =>
                    texts.Select(_ => new float[1536]).ToList() as IReadOnlyList<float[]>);

            _unitOfWorkMock.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
            _unitOfWorkMock.Setup(u => u.ClearChangeTracker());

            // Act
            var result = await _orchestrator.IndexProjectStreamingAsync(projectId, tempDir);

            // Assert
            result.FilesProcessed.Should().Be(1); // Only src/main.cs
            processedFiles.Should().ContainSingle();
            processedFiles.Should().NotContain(p => p.Contains("node_modules"));
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, true);
            }
        }
    }

    private static Dictionary<string, string> GenerateLargeFileSet(int fileCount)
    {
        var files = new Dictionary<string, string>();
        for (var i = 0; i < fileCount; i++)
        {
            var content = $@"
namespace TestNamespace
{{
    public class TestClass{i}
    {{
        public void Method1() {{ }}
        public void Method2() {{ }}
        public void Method3() {{ }}
    }}
}}";
            files[$"src/File{i}.cs"] = content;
        }
        return files;
    }
}
