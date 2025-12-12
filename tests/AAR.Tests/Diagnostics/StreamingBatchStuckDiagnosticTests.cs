// =============================================================================
// AAR.Tests - Diagnostics/StreamingBatchStuckDiagnosticTests.cs
// Tests to reproduce and diagnose the stuck batch 21/33 issue
// =============================================================================

using System.Diagnostics;
using System.Text;
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

namespace AAR.Tests.Diagnostics;

/// <summary>
/// Diagnostic tests to reproduce and analyze the batch 21/33 stuck issue.
/// These tests simulate the exact conditions that cause the worker to hang.
/// </summary>
public class StreamingBatchStuckDiagnosticTests : IDisposable
{
    private readonly ITestOutputHelper _output;
    private readonly string _testDirectory;
    private readonly List<string> _logMessages = new();
    
    public StreamingBatchStuckDiagnosticTests(ITestOutputHelper output)
    {
        _output = output;
        _testDirectory = Path.Combine(Path.GetTempPath(), $"AAR_DiagTest_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_testDirectory);
    }

    public void Dispose()
    {
        try { Directory.Delete(_testDirectory, true); } catch { /* ignore */ }
    }

    /// <summary>
    /// Reproduces the exact stuck batch scenario with 33 batches of files.
    /// This test identifies where processing hangs.
    /// </summary>
    [Fact]
    public async Task ReproduceStuckBatch_33Batches_ShouldCompleteOrTimeout()
    {
        // Arrange - Create 330 files (33 batches of 10)
        _output.WriteLine("=== REPRODUCING STUCK BATCH SCENARIO ===");
        _output.WriteLine($"Test directory: {_testDirectory}");
        
        var filesCreated = CreateSimulatedProjectFiles(330);
        _output.WriteLine($"Created {filesCreated} test files");
        
        var chunker = CreateChunker();
        var orchestrator = CreateOrchestrator(chunker);
        var projectId = Guid.NewGuid();
        
        // Use timeout to detect stuck state
        using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(5));
        var stopwatch = Stopwatch.StartNew();
        var batchStartTimes = new Dictionary<int, DateTime>();
        var lastBatchSeen = 0;
        
        // Monitor task to detect stuck batches
        var monitorTask = Task.Run(async () =>
        {
            while (!cts.Token.IsCancellationRequested)
            {
                await Task.Delay(2000, cts.Token);
                
                var currentBatch = _logMessages
                    .Where(m => m.Contains("Processing streaming batch"))
                    .Select(m => 
                    {
                        var match = System.Text.RegularExpressions.Regex.Match(m, @"batch (\d+)/");
                        return match.Success ? int.Parse(match.Groups[1].Value) : 0;
                    })
                    .DefaultIfEmpty(0)
                    .Max();
                
                if (currentBatch > lastBatchSeen)
                {
                    lastBatchSeen = currentBatch;
                    batchStartTimes[currentBatch] = DateTime.UtcNow;
                    _output.WriteLine($"[{stopwatch.Elapsed:mm\\:ss}] Batch {currentBatch} started");
                }
                else if (currentBatch > 0 && batchStartTimes.TryGetValue(currentBatch, out var started))
                {
                    var stuckTime = DateTime.UtcNow - started;
                    if (stuckTime > TimeSpan.FromSeconds(30))
                    {
                        _output.WriteLine($"[{stopwatch.Elapsed:mm\\:ss}] WARNING: Batch {currentBatch} appears stuck for {stuckTime.TotalSeconds:F0}s");
                    }
                }
            }
        }, cts.Token);
        
        // Act
        IndexingResult? result = null;
        Exception? caughtException = null;
        
        try
        {
            result = await orchestrator.IndexProjectStreamingAsync(projectId, _testDirectory, cts.Token);
        }
        catch (OperationCanceledException)
        {
            _output.WriteLine($"[{stopwatch.Elapsed:mm\\:ss}] TIMEOUT - Processing did not complete in 5 minutes");
            _output.WriteLine($"Last batch seen: {lastBatchSeen}");
            _output.WriteLine("");
            _output.WriteLine("=== DIAGNOSTIC LOG EXCERPT (last 50 lines) ===");
            foreach (var log in _logMessages.TakeLast(50))
            {
                _output.WriteLine(log);
            }
        }
        catch (Exception ex)
        {
            caughtException = ex;
            _output.WriteLine($"[{stopwatch.Elapsed:mm\\:ss}] EXCEPTION: {ex.GetType().Name}: {ex.Message}");
        }
        finally
        {
            stopwatch.Stop();
            await cts.CancelAsync();
            try { await monitorTask; } catch { /* ignore */ }
        }
        
        // Assert
        _output.WriteLine("");
        _output.WriteLine("=== RESULTS ===");
        _output.WriteLine($"Total time: {stopwatch.Elapsed}");
        _output.WriteLine($"Files processed: {result?.FilesProcessed ?? 0}");
        _output.WriteLine($"Chunks created: {result?.ChunksCreated ?? 0}");
        _output.WriteLine($"Errors: {result?.Errors?.Count ?? 0}");
        
        if (result?.Errors?.Any() == true)
        {
            _output.WriteLine("Error details:");
            foreach (var error in result.Errors)
            {
                _output.WriteLine($"  - {error}");
            }
        }
        
        // This should complete - if it times out, we have reproduced the bug
        result.Should().NotBeNull("Processing should complete without timeout");
        result!.FilesProcessed.Should().Be(330, "All files should be processed");
    }

    /// <summary>
    /// Tests that problematic file patterns are handled gracefully.
    /// </summary>
    [Theory]
    [InlineData("LongSingleLine", 100000)]  // 100KB single line
    [InlineData("DeeplyNested", 0)]         // Complex nested classes
    [InlineData("UnicodeMixed", 0)]         // Unicode characters
    [InlineData("BinaryLooking", 0)]        // Binary-like content
    public async Task ChunkProblematicFile_ShouldNotHang(string fileType, int lineLength)
    {
        // Arrange
        var content = fileType switch
        {
            "LongSingleLine" => "// " + new string('A', lineLength),
            "DeeplyNested" => CreateDeeplyNestedClass(10),
            "UnicodeMixed" => CreateUnicodeContent(),
            "BinaryLooking" => CreateBinaryLikeContent(),
            _ => throw new ArgumentException($"Unknown file type: {fileType}")
        };
        
        var filePath = Path.Combine(_testDirectory, $"{fileType}.cs");
        await File.WriteAllTextAsync(filePath, content);
        
        var chunker = CreateChunker();
        var projectId = Guid.NewGuid();
        
        // Act - should complete within timeout
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var stopwatch = Stopwatch.StartNew();
        
        IReadOnlyList<ChunkInfo>? chunks = null;
        Exception? exception = null;
        
        try
        {
            chunks = await chunker.ChunkFileAsync(filePath, content, projectId, cts.Token);
        }
        catch (Exception ex)
        {
            exception = ex;
        }
        
        stopwatch.Stop();
        
        // Assert
        _output.WriteLine($"{fileType}: {stopwatch.ElapsedMilliseconds}ms, {chunks?.Count ?? 0} chunks, " +
                         $"exception: {exception?.GetType().Name ?? "none"}");
        
        stopwatch.ElapsedMilliseconds.Should().BeLessThan(30000, 
            $"Chunking {fileType} should complete within 30 seconds");
        
        if (exception != null && exception is not OperationCanceledException)
        {
            Assert.Fail($"Unexpected exception: {exception}");
        }
    }

    /// <summary>
    /// Tests that embedding service doesn't deadlock under load.
    /// </summary>
    [Fact]
    public async Task EmbeddingService_HighConcurrency_ShouldNotDeadlock()
    {
        // Arrange
        var mockEmbedding = new MockEmbeddingService();
        mockEmbedding.SetDelay(100); // 100ms per embedding to simulate latency
        
        var texts = Enumerable.Range(1, 100)
            .Select(i => $"Test content for chunk {i} with some meaningful text")
            .ToList();
        
        // Act - parallel embedding requests
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var stopwatch = Stopwatch.StartNew();
        
        var tasks = texts.Select(async text =>
        {
            await mockEmbedding.CreateEmbeddingAsync(text, cts.Token);
        });
        
        await Task.WhenAll(tasks);
        stopwatch.Stop();
        
        // Assert
        _output.WriteLine($"100 embeddings in {stopwatch.ElapsedMilliseconds}ms");
        stopwatch.ElapsedMilliseconds.Should().BeLessThan(30000);
    }

    /// <summary>
    /// Tests file enumeration doesn't miss or duplicate files.
    /// </summary>
    [Fact]
    public void FileEnumeration_LargeProject_ShouldBeConsistent()
    {
        // Arrange
        var fileCount = CreateSimulatedProjectFiles(330);
        
        // Act - enumerate multiple times
        var counts = new List<int>();
        for (int i = 0; i < 3; i++)
        {
            var files = EnumerateSourceFiles(_testDirectory).ToList();
            counts.Add(files.Count);
            _output.WriteLine($"Enumeration {i + 1}: {files.Count} files");
        }
        
        // Assert
        counts.Should().AllBeEquivalentTo(counts[0], "File enumeration should be consistent");
    }

    #region Helper Methods

    private int CreateSimulatedProjectFiles(int targetCount)
    {
        var count = 0;
        var projects = new[] { "Proj1", "Proj2", "Proj3", "Proj4", "Proj5", "Proj6" };
        
        foreach (var project in projects)
        {
            var projectPath = Path.Combine(_testDirectory, project);
            Directory.CreateDirectory(projectPath);
            
            // Controllers
            var controllersPath = Path.Combine(projectPath, "Controllers");
            Directory.CreateDirectory(controllersPath);
            for (int i = 1; i <= 8; i++)
            {
                count++;
                File.WriteAllText(
                    Path.Combine(controllersPath, $"Controller{i}.cs"),
                    GenerateControllerContent(project, i));
                if (count >= targetCount) return count;
            }
            
            // Services
            var servicesPath = Path.Combine(projectPath, "Services");
            Directory.CreateDirectory(servicesPath);
            for (int i = 1; i <= 6; i++)
            {
                count++;
                File.WriteAllText(
                    Path.Combine(servicesPath, $"Service{i}.cs"),
                    GenerateServiceContent(project, i));
                if (count >= targetCount) return count;
            }
            
            // Models
            var modelsPath = Path.Combine(projectPath, "Models");
            Directory.CreateDirectory(modelsPath);
            for (int i = 1; i <= 5; i++)
            {
                count++;
                File.WriteAllText(
                    Path.Combine(modelsPath, $"Model{i}.cs"),
                    GenerateModelContent(project, i));
                if (count >= targetCount) return count;
            }
        }
        
        // Pad with small helpers
        var helpersPath = Path.Combine(_testDirectory, "Helpers");
        Directory.CreateDirectory(helpersPath);
        while (count < targetCount)
        {
            count++;
            File.WriteAllText(
                Path.Combine(helpersPath, $"Helper{count}.cs"),
                $"namespace Helpers {{ public static class Helper{count} {{ public static int Value => {count}; }} }}");
        }
        
        return count;
    }

    private string GenerateControllerContent(string project, int index) => $@"
using Microsoft.AspNetCore.Mvc;
using System.Threading.Tasks;

namespace {project}.Controllers
{{
    public class Controller{index}Controller : Controller
    {{
        [HttpGet]
        public async Task<IActionResult> Index()
        {{
            await Task.Delay(1);
            return View();
        }}
        
        [HttpPost]
        public async Task<IActionResult> Process([FromBody] object data)
        {{
            await Task.Delay(1);
            return Ok(data);
        }}
    }}
}}";

    private string GenerateServiceContent(string project, int index) => $@"
using System.Threading.Tasks;
using System.Collections.Generic;

namespace {project}.Services
{{
    public interface IService{index}
    {{
        Task<IEnumerable<object>> GetAllAsync();
    }}
    
    public class Service{index} : IService{index}
    {{
        public async Task<IEnumerable<object>> GetAllAsync()
        {{
            await Task.Delay(1);
            return new List<object>();
        }}
    }}
}}";

    private string GenerateModelContent(string project, int index) => $@"
using System;
using System.ComponentModel.DataAnnotations;

namespace {project}.Models
{{
    public class Model{index}
    {{
        [Key]
        public Guid Id {{ get; set; }}
        
        [Required]
        public string Name {{ get; set; }}
        
        public DateTime CreatedAt {{ get; set; }}
    }}
}}";

    private string CreateDeeplyNestedClass(int depth)
    {
        var sb = new StringBuilder();
        sb.AppendLine("namespace DeepNest {");
        
        for (int i = 0; i < depth; i++)
        {
            sb.AppendLine($"public class Level{i} {{");
        }
        
        sb.AppendLine("public void DeepMethod() { }");
        
        for (int i = 0; i < depth; i++)
        {
            sb.AppendLine("}");
        }
        
        sb.AppendLine("}");
        return sb.ToString();
    }

    private string CreateUnicodeContent() => @"
namespace Unicode
{
    // 你好世界 مرحبا العالم שלום עולם 🌍🎉
    public class UnicodeTest
    {
        public string Greeting => ""Hello 世界 🌍"";
        public string Arabic => ""مرحبا"";
        public string Hebrew => ""שלום"";
    }
}";

    private string CreateBinaryLikeContent()
    {
        var sb = new StringBuilder();
        sb.AppendLine("namespace Binary {");
        sb.AppendLine("public class BinaryData {");
        sb.AppendLine("// Binary-like content that might confuse parsers:");
        sb.Append("public byte[] Data = { ");
        for (int i = 0; i < 1000; i++)
        {
            sb.Append($"0x{i % 256:X2}, ");
        }
        sb.AppendLine("};");
        sb.AppendLine("}}");
        return sb.ToString();
    }

    private static readonly HashSet<string> SourceExtensions = new()
    {
        ".cs", ".ts", ".tsx", ".js", ".jsx", ".py", ".java", ".go", ".rs"
    };

    private static readonly HashSet<string> ExcludedDirs = new()
    {
        "node_modules", "bin", "obj", ".git", ".vs"
    };

    private IEnumerable<(string FullPath, string RelativePath)> EnumerateSourceFiles(string directory)
    {
        foreach (var file in Directory.EnumerateFiles(directory, "*.*", SearchOption.AllDirectories))
        {
            var ext = Path.GetExtension(file).ToLowerInvariant();
            if (!SourceExtensions.Contains(ext)) continue;
            
            var relative = Path.GetRelativePath(directory, file);
            var skip = false;
            foreach (var excluded in ExcludedDirs)
            {
                if (relative.Contains($"{Path.DirectorySeparatorChar}{excluded}{Path.DirectorySeparatorChar}") ||
                    relative.StartsWith($"{excluded}{Path.DirectorySeparatorChar}"))
                {
                    skip = true;
                    break;
                }
            }
            
            if (!skip) yield return (file, relative);
        }
    }

    private SemanticChunker CreateChunker()
    {
        var loggerMock = new Mock<ILogger<SemanticChunker>>();
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
                if (level >= LogLevel.Information)
                {
                    _output.WriteLine(message);
                }
            });

        var tokenizerFactory = new Mock<ITokenizerFactory>();
        tokenizerFactory.Setup(f => f.Create()).Returns(new TikTokenizer());

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

    private RetrievalOrchestrator CreateOrchestrator(IChunker chunker)
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
                var message = $"[{DateTime.Now:HH:mm:ss}] [{level}] {state}";
                _logMessages.Add(message);
                _output.WriteLine(message);
            });

        var mockEmbedding = new MockEmbeddingService();
        var mockVectorStore = new InMemoryVectorStore();
        var mockOpenAi = new MockOpenAiService();
        
        var chunkRepoMock = new Mock<IChunkRepository>();
        chunkRepoMock.Setup(r => r.AddRangeAsync(It.IsAny<IEnumerable<Chunk>>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        chunkRepoMock.Setup(r => r.DeleteByProjectIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        
        var unitOfWorkMock = new Mock<IUnitOfWork>();
        unitOfWorkMock.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);
        unitOfWorkMock.Setup(u => u.ClearChangeTracker());
        
        var tokenizerFactory = new Mock<ITokenizerFactory>();
        tokenizerFactory.Setup(f => f.Create()).Returns(new TikTokenizer());
        
        var progressServiceMock = new Mock<IJobProgressService>();
        progressServiceMock.Setup(p => p.ReportProgressAsync(It.IsAny<JobProgressUpdate>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var watchdogMock = new Mock<IBatchProcessingWatchdog>();
        watchdogMock.Setup(w => w.TrackBatch(It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .Returns((Guid _, int _, int _, CancellationToken ct) => CancellationTokenSource.CreateLinkedTokenSource(ct));

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
            watchdogMock.Object,
            chunkerOptions,
            routerOptions,
            loggerMock.Object);
    }

    #endregion
}

// TikTokenizer stub for testing
file class TikTokenizer : ITokenizer
{
    public int CountTokens(string text) => Math.Max(1, text.Length / 4);
    public int[] Encode(string text) => text.Select(_ => 1).ToArray();
    public string Decode(int[] tokens) => new string('x', tokens.Length);
    public string EncodingName => "test-tokenizer";
    public bool IsHeuristic => true;
}
