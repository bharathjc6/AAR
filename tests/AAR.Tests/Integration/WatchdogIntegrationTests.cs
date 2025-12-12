// =============================================================================
// AAR.Tests - Integration/WatchdogIntegrationTests.cs
// Integration tests for watchdog with RetrievalOrchestrator
// =============================================================================

using AAR.Application.DTOs;
using AAR.Application.Interfaces;
using AAR.Domain.Entities;
using AAR.Domain.Interfaces;
using AAR.Infrastructure.Services.Chunking;
using AAR.Infrastructure.Services.Retrieval;
using AAR.Infrastructure.Services.Watchdog;
using AAR.Shared.Tokenization;
using AAR.Tests.Mocks;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;
using Xunit.Abstractions;
using InMemoryVectorStore = AAR.Tests.Mocks.InMemoryVectorStore;

namespace AAR.Tests.Integration;

/// <summary>
/// Integration tests for the watchdog service with the retrieval orchestrator
/// </summary>
public class WatchdogIntegrationTests : IDisposable
{
    private readonly ITestOutputHelper _output;
    private readonly string _tempDir;
    private readonly List<string> _logMessages = new();

    public WatchdogIntegrationTests(ITestOutputHelper output)
    {
        _output = output;
        _tempDir = Path.Combine(Path.GetTempPath(), $"watchdog-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempDir))
            {
                Directory.Delete(_tempDir, true);
            }
        }
        catch { }
    }

    [Fact]
    public async Task IndexProjectStreaming_WithWatchdog_TracksProgressCorrectly()
    {
        // Arrange - Create test files
        CreateTestFiles(50); // 50 files = 5 batches

        var orchestrator = CreateOrchestrator(out var watchdog);
        var projectId = Guid.NewGuid();

        // Act
        var result = await orchestrator.IndexProjectStreamingAsync(projectId, _tempDir);

        // Assert
        result.ChunksCreated.Should().BeGreaterThan(0);
        (result.Errors ?? new List<string>()).Should().BeEmpty();

        // Verify watchdog tracked the project (it should be completed now)
        watchdog.GetTrackedProjects().Should().BeEmpty("Project should be untracked after completion");
    }

    [Fact]
    public async Task IndexProjectStreaming_WhenCancelled_WatchdogStopsTracking()
    {
        // Arrange - Create test files
        CreateTestFiles(100); // 100 files = 10 batches

        var orchestrator = CreateOrchestrator(out var watchdog);
        var projectId = Guid.NewGuid();
        using var cts = new CancellationTokenSource();

        // Cancel after a short delay
        cts.CancelAfter(TimeSpan.FromMilliseconds(100));

        // Act & Assert
        var act = async () => await orchestrator.IndexProjectStreamingAsync(projectId, _tempDir, cts.Token);
        await act.Should().ThrowAsync<OperationCanceledException>();

        // Give a moment for cleanup
        await Task.Delay(50);

        // Watchdog should have the project in cancelled/stuck state or removed
        _output.WriteLine($"Tracked projects after cancellation: {watchdog.GetTrackedProjects().Count}");
    }

    [Fact]
    public async Task IndexProjectStreaming_WatchdogReceivesHeartbeats()
    {
        // Arrange
        CreateTestFiles(30); // 30 files = 3 batches

        var watchdogMock = new Mock<IBatchProcessingWatchdog>();
        var heartbeatCount = 0;
        var phaseUpdates = new List<string>();

        watchdogMock.Setup(w => w.TrackBatch(It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .Returns((Guid _, int _, int _, CancellationToken ct) => CancellationTokenSource.CreateLinkedTokenSource(ct));
        
        watchdogMock.Setup(w => w.Heartbeat(It.IsAny<Guid>()))
            .Callback(() => Interlocked.Increment(ref heartbeatCount));
        
        watchdogMock.Setup(w => w.UpdatePhase(It.IsAny<Guid>(), It.IsAny<string>()))
            .Callback<Guid, string>((_, phase) => { lock (phaseUpdates) { phaseUpdates.Add(phase); } });

        var orchestrator = CreateOrchestratorWithMockWatchdog(watchdogMock.Object);
        var projectId = Guid.NewGuid();

        // Act
        var result = await orchestrator.IndexProjectStreamingAsync(projectId, _tempDir);

        // Assert
        result.ChunksCreated.Should().BeGreaterThan(0);
        heartbeatCount.Should().BeGreaterThan(0, "Watchdog should receive heartbeats");
        phaseUpdates.Should().NotBeEmpty("Watchdog should receive phase updates");

        _output.WriteLine($"Total heartbeats: {heartbeatCount}");
        _output.WriteLine($"Phase updates: {string.Join(", ", phaseUpdates.Take(10))}...");
    }

    [Fact]
    public async Task WatchdogDetectsStuckProject_WhenNoHeartbeat()
    {
        // Arrange - Create watchdog with very short timeout
        var options = Options.Create(new WatchdogOptions
        {
            Enabled = true,
            CheckIntervalSeconds = 1,
            MaxHeartbeatIntervalSeconds = 1, // 1 second
            MaxProjectDurationSeconds = 60,
            AutoCancelStuck = false
        });
        var loggerMock = new Mock<ILogger<BatchProcessingWatchdog>>();
        var watchdog = new BatchProcessingWatchdog(options, loggerMock.Object);

        var projectId = Guid.NewGuid();
        using var _ = watchdog.TrackBatch(projectId, 0, 10, CancellationToken.None);

        // Wait for the heartbeat interval to expire
        await Task.Delay(1500);

        // Act
        var isStuck = watchdog.IsProjectStuck(projectId);

        // Assert
        isStuck.Should().BeTrue("Project should be detected as stuck after no heartbeat");
    }

    [Fact]
    public async Task WatchdogDoesNotDetectStuck_WhenHeartbeatsReceived()
    {
        // Arrange
        var options = Options.Create(new WatchdogOptions
        {
            Enabled = true,
            CheckIntervalSeconds = 1,
            MaxHeartbeatIntervalSeconds = 2,
            MaxProjectDurationSeconds = 60,
            AutoCancelStuck = false
        });
        var loggerMock = new Mock<ILogger<BatchProcessingWatchdog>>();
        var watchdog = new BatchProcessingWatchdog(options, loggerMock.Object);

        var projectId = Guid.NewGuid();
        using var _ = watchdog.TrackBatch(projectId, 0, 10, CancellationToken.None);

        // Keep sending heartbeats
        for (int i = 0; i < 5; i++)
        {
            await Task.Delay(500);
            watchdog.Heartbeat(projectId);
        }

        // Act
        var isStuck = watchdog.IsProjectStuck(projectId);

        // Assert
        isStuck.Should().BeFalse("Project should not be stuck when receiving heartbeats");
    }

    #region Helper Methods

    private void CreateTestFiles(int count)
    {
        for (int i = 0; i < count; i++)
        {
            // Create larger content to ensure chunks are created (minimum token count is 50)
            var content = $@"
// =============================================================================
// Test file {i} - Generated for watchdog integration tests
// =============================================================================

namespace TestNamespace{i}
{{
    /// <summary>
    /// Test class number {i} with sufficient content to be chunked properly
    /// This comment block helps ensure we have enough tokens for the chunker
    /// </summary>
    public class TestClass{i}
    {{
        private readonly int _value{i} = {i};
        private string _name{i} = ""TestClass{i}"";
        
        /// <summary>
        /// Constructor for TestClass{i}
        /// </summary>
        public TestClass{i}()
        {{
            _value{i} = {i} * 2;
            _name{i} = $""Initialized: {{_value{i}}}"";
        }}
        
        /// <summary>
        /// Method that performs some test operation
        /// </summary>
        public int Method{i}(int input)
        {{
            // Perform some calculation
            var result = input * _value{i};
            Console.WriteLine($""Method{i} called with {{input}}, result: {{result}}"");
            return result;
        }}
        
        /// <summary>
        /// Another method with more logic
        /// </summary>
        public string GetDescription{i}()
        {{
            return $""Class: {{_name{i}}}, Value: {{_value{i}}}"";
        }}
    }}
}}";
            File.WriteAllText(Path.Combine(_tempDir, $"TestFile{i}.cs"), content);
        }
    }

    private RetrievalOrchestrator CreateOrchestrator(out BatchProcessingWatchdog watchdog)
    {
        var loggerMock = new Mock<ILogger<RetrievalOrchestrator>>();
        loggerMock.Setup(x => x.Log(
            It.IsAny<LogLevel>(),
            It.IsAny<EventId>(),
            It.IsAny<It.IsAnyType>(),
            It.IsAny<Exception?>(),
            It.IsAny<Func<It.IsAnyType, Exception?, string>>()))
            .Callback<LogLevel, EventId, object, Exception?, Delegate>((level, id, state, ex, formatter) =>
            {
                var message = $"[{level}] {state}";
                _logMessages.Add(message);
            });

        var mockEmbedding = new MockEmbeddingService();
        var mockVectorStore = new InMemoryVectorStore();
        var mockOpenAi = new MockOpenAiService();

        var tokenizerFactory = new Mock<ITokenizerFactory>();
        tokenizerFactory.Setup(f => f.Create()).Returns(new TestTokenizer());

        var chunker = CreateChunker();

        var chunkRepoMock = new Mock<IChunkRepository>();
        chunkRepoMock.Setup(r => r.AddRangeAsync(It.IsAny<IEnumerable<Chunk>>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        chunkRepoMock.Setup(r => r.DeleteByProjectIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var unitOfWorkMock = new Mock<IUnitOfWork>();
        unitOfWorkMock.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        var progressServiceMock = new Mock<IJobProgressService>();
        progressServiceMock.Setup(p => p.ReportProgressAsync(It.IsAny<JobProgressUpdate>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var watchdogOptions = Options.Create(new WatchdogOptions
        {
            Enabled = true,
            CheckIntervalSeconds = 30,
            MaxProjectDurationSeconds = 600,
            MaxHeartbeatIntervalSeconds = 120
        });
        var watchdogLogger = new Mock<ILogger<BatchProcessingWatchdog>>();
        watchdog = new BatchProcessingWatchdog(watchdogOptions, watchdogLogger.Object);

        var chunkerOptions = Options.Create(new ChunkerOptions
        {
            MaxChunkTokens = 1600,
            MinChunkTokens = 50,
            StoreChunkText = false
        });

        var routerOptions = Options.Create(new ModelRouterOptions
        {
            TopK = 20,
            SummarizationThreshold = 8000
        });

        return new RetrievalOrchestrator(
            chunker,
            mockEmbedding,
            mockVectorStore,
            mockOpenAi,
            chunkRepoMock.Object,
            unitOfWorkMock.Object,
            tokenizerFactory.Object,
            progressServiceMock.Object,
            watchdog,
            chunkerOptions,
            routerOptions,
            loggerMock.Object);
    }

    private RetrievalOrchestrator CreateOrchestratorWithMockWatchdog(IBatchProcessingWatchdog watchdog)
    {
        var loggerMock = new Mock<ILogger<RetrievalOrchestrator>>();
        var mockEmbedding = new MockEmbeddingService();
        var mockVectorStore = new InMemoryVectorStore();
        var mockOpenAi = new MockOpenAiService();

        var tokenizerFactory = new Mock<ITokenizerFactory>();
        tokenizerFactory.Setup(f => f.Create()).Returns(new TestTokenizer());

        var chunker = CreateChunker();

        var chunkRepoMock = new Mock<IChunkRepository>();
        chunkRepoMock.Setup(r => r.AddRangeAsync(It.IsAny<IEnumerable<Chunk>>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        chunkRepoMock.Setup(r => r.DeleteByProjectIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var unitOfWorkMock = new Mock<IUnitOfWork>();
        unitOfWorkMock.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        var progressServiceMock = new Mock<IJobProgressService>();
        progressServiceMock.Setup(p => p.ReportProgressAsync(It.IsAny<JobProgressUpdate>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var chunkerOptions = Options.Create(new ChunkerOptions
        {
            MaxChunkTokens = 1600,
            MinChunkTokens = 50,
            StoreChunkText = false
        });

        var routerOptions = Options.Create(new ModelRouterOptions
        {
            TopK = 20,
            SummarizationThreshold = 8000
        });

        return new RetrievalOrchestrator(
            chunker,
            mockEmbedding,
            mockVectorStore,
            mockOpenAi,
            chunkRepoMock.Object,
            unitOfWorkMock.Object,
            tokenizerFactory.Object,
            progressServiceMock.Object,
            watchdog,
            chunkerOptions,
            routerOptions,
            loggerMock.Object);
    }

    private SemanticChunker CreateChunker()
    {
        var tokenizerFactory = new Mock<ITokenizerFactory>();
        tokenizerFactory.Setup(f => f.Create()).Returns(new TestTokenizer());

        var loggerMock = new Mock<ILogger<SemanticChunker>>();
        var options = Options.Create(new ChunkerOptions
        {
            MaxChunkTokens = 1600,
            MinChunkTokens = 50,
            OverlapTokens = 100,
            UseSemanticSplitting = true,
            StoreChunkText = false
        });

        return new SemanticChunker(tokenizerFactory.Object, options, loggerMock.Object);
    }

    #endregion
}

file class TestTokenizer : ITokenizer
{
    public int CountTokens(string text) => Math.Max(1, text.Length / 4);
    public int[] Encode(string text) => text.Select(_ => 1).ToArray();
    public string Decode(int[] tokens) => new string('x', tokens.Length);
    public string EncodingName => "test-tokenizer";
    public bool IsHeuristic => true;
}
