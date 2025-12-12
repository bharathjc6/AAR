// =============================================================================
// AAR.Tests - Integration/LargeRepoSimulationTests.cs
// Integration tests simulating large repository processing
// =============================================================================

using AAR.Application.Configuration;
using AAR.Application.DTOs;
using AAR.Application.Interfaces;
using AAR.Infrastructure.Services.Memory;
using AAR.Infrastructure.Services.Routing;
using AAR.Shared.Tokenization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;

namespace AAR.Tests.Integration;

/// <summary>
/// Integration tests that simulate processing large repositories with many files,
/// including files exceeding thresholds, to verify memory safety and routing.
/// </summary>
public class LargeRepoSimulationTests : IDisposable
{
    private readonly string _testDirectory;
    private readonly Mock<IRagRiskFilter> _riskFilterMock;
    private readonly Mock<ITokenizerFactory> _tokenizerFactoryMock;
    private readonly Mock<ITokenizer> _tokenizerMock;
    private readonly Mock<ILogger<FileAnalysisRouter>> _loggerMock;

    public LargeRepoSimulationTests()
    {
        _testDirectory = Path.Combine(Path.GetTempPath(), $"AAR_LargeRepoTest_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_testDirectory);
        
        _riskFilterMock = new Mock<IRagRiskFilter>();
        _tokenizerFactoryMock = new Mock<ITokenizerFactory>();
        _tokenizerMock = new Mock<ITokenizer>();
        _loggerMock = new Mock<ILogger<FileAnalysisRouter>>();

        _tokenizerFactoryMock.Setup(f => f.Create()).Returns(_tokenizerMock.Object);
        _tokenizerMock.Setup(t => t.CountTokens(It.IsAny<string>())).Returns(100);
        _riskFilterMock.Setup(r => r.ComputeRiskScoresAsync(
            It.IsAny<Guid>(),
            It.IsAny<IEnumerable<string>>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, float>());
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_testDirectory))
            {
                Directory.Delete(_testDirectory, recursive: true);
            }
        }
        catch
        {
            // Ignore cleanup failures in tests
        }
    }

    private FileAnalysisRouter CreateRouter(
        int directSendKB = 10,
        int ragChunkKB = 200,
        bool allowLargeFiles = false)
    {
        var ragOptions = Options.Create(new RagProcessingOptions
        {
            DirectSendThresholdBytes = directSendKB * 1024,
            RagChunkThresholdBytes = ragChunkKB * 1024,
            AllowLargeFiles = allowLargeFiles
        });

        var approvalOptions = Options.Create(new JobApprovalOptions
        {
            WarnThresholdTokens = 500_000,
            ApprovalThresholdTokens = 2_000_000
        });

        var scaleLimits = Options.Create(new ScaleLimitsOptions());

        return new FileAnalysisRouter(
            ragOptions,
            approvalOptions,
            scaleLimits,
            _riskFilterMock.Object,
            _tokenizerFactoryMock.Object,
            _loggerMock.Object);
    }

    private void CreateTestFile(string relativePath, int sizeBytes)
    {
        var fullPath = Path.Combine(_testDirectory, relativePath);
        var directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        // Create file with specified size
        using var fs = File.Create(fullPath);
        if (sizeBytes > 0)
        {
            var buffer = new byte[Math.Min(sizeBytes, 8192)];
            var random = new Random(42); // Deterministic for tests
            var remaining = sizeBytes;

            while (remaining > 0)
            {
                var toWrite = Math.Min(remaining, buffer.Length);
                random.NextBytes(buffer);
                fs.Write(buffer, 0, toWrite);
                remaining -= toWrite;
            }
        }
    }

    [Fact]
    public async Task LargeRepo_WithManySmallFiles_AllProcessedAsDirectSend()
    {
        // Arrange - Create 100 small files (< 10KB each)
        for (int i = 0; i < 100; i++)
        {
            CreateTestFile($"src/file{i}.cs", 5 * 1024); // 5KB each
        }

        var router = CreateRouter();

        // Act
        var estimate = await router.EstimateAnalysisAsync(_testDirectory);

        // Assert
        Assert.Equal(100, estimate.DirectSendCount);
        Assert.Equal(0, estimate.RagChunkCount);
        Assert.Equal(0, estimate.SkippedCount);
    }

    [Fact]
    public async Task LargeRepo_WithMixedFileSizes_CorrectRouting()
    {
        // Arrange - Create files of various sizes
        // 50 small files (< 10KB) -> DirectSend
        for (int i = 0; i < 50; i++)
        {
            CreateTestFile($"src/small/file{i}.cs", 5 * 1024); // 5KB
        }

        // 30 medium files (10KB - 200KB) -> RagChunks
        for (int i = 0; i < 30; i++)
        {
            CreateTestFile($"src/medium/file{i}.cs", 50 * 1024); // 50KB
        }

        // 20 large files (> 200KB) -> Skip
        for (int i = 0; i < 20; i++)
        {
            CreateTestFile($"src/large/file{i}.json", 300 * 1024); // 300KB
        }

        var router = CreateRouter();

        // Act
        var estimate = await router.EstimateAnalysisAsync(_testDirectory);

        // Assert
        Assert.Equal(50, estimate.DirectSendCount);
        Assert.Equal(30, estimate.RagChunkCount);
        Assert.Equal(20, estimate.SkippedCount);
        Assert.Equal(20, estimate.SkippedFiles.Count);
    }

    [Fact]
    public async Task LargeRepo_FilesAtExactThresholds_CorrectBoundaryBehavior()
    {
        // Arrange - Create files exactly at thresholds
        CreateTestFile("small_9kb.cs", 9 * 1024);        // Under 10KB -> Should be DirectSend
        CreateTestFile("at_10kb.cs", 10 * 1024);         // At 10KB -> Should be RagChunks (>= threshold)
        CreateTestFile("at_200kb.cs", 200 * 1024);       // At 200KB -> Should be RagChunks
        CreateTestFile("over_200kb.cs", 200 * 1024 + 1); // Over 200KB -> Should be Skipped

        var router = CreateRouter();

        // Act
        var estimate = await router.EstimateAnalysisAsync(_testDirectory);

        // Assert
        Assert.Equal(1, estimate.DirectSendCount);  // small_9kb.cs
        Assert.Equal(2, estimate.RagChunkCount);    // at_10kb.cs + at_200kb.cs
        Assert.Equal(1, estimate.SkippedCount);     // over_200kb.cs
    }

    [Fact]
    public async Task LargeRepo_WithBinaryFiles_AutomaticallySkipped()
    {
        // Arrange - Create text and binary files
        CreateTestFile("code.cs", 5 * 1024);           // Text file
        CreateTestFile("image.png", 15 * 1024);        // Binary file (would be RagChunks by size)
        CreateTestFile("archive.zip", 50 * 1024);      // Binary file
        CreateTestFile("database.dll", 100 * 1024);    // Binary file

        var router = CreateRouter();

        // Act
        var estimate = await router.EstimateAnalysisAsync(_testDirectory);

        // Assert - Binary files should be skipped regardless of size
        Assert.Equal(1, estimate.DirectSendCount);  // code.cs
        Assert.Equal(0, estimate.RagChunkCount);    // No text files in range
        Assert.Equal(3, estimate.SkippedCount);     // All binary files
    }

    [Fact]
    public async Task LargeRepo_ExcludedDirectories_NotProcessed()
    {
        // Arrange - Create files in normal and excluded directories
        CreateTestFile("src/app.cs", 5 * 1024);
        CreateTestFile("node_modules/package/index.js", 5 * 1024);
        CreateTestFile("bin/debug/app.dll", 5 * 1024);
        CreateTestFile(".git/objects/abc123", 5 * 1024);
        CreateTestFile("vendor/lib/util.php", 5 * 1024);

        var router = CreateRouter();

        // Act
        var estimate = await router.EstimateAnalysisAsync(_testDirectory);

        // Assert - Only src/app.cs should be processed
        Assert.Equal(1, estimate.DirectSendCount);
        Assert.Equal(0, estimate.RagChunkCount);
    }

    [Fact]
    public async Task LargeRepo_VeryLargeFiles_CorrectlySkipped()
    {
        // Arrange - Create some very large files
        CreateTestFile("small.cs", 1024);              // 1KB
        CreateTestFile("huge_data.json", 10 * 1024 * 1024); // 10MB

        var router = CreateRouter();

        // Act
        var estimate = await router.EstimateAnalysisAsync(_testDirectory);

        // Assert
        Assert.Equal(1, estimate.DirectSendCount);
        Assert.Equal(1, estimate.SkippedCount);
        
        var skippedFile = estimate.SkippedFiles.FirstOrDefault(f => f.FilePath.Contains("huge_data"));
        Assert.NotNull(skippedFile);
        Assert.Contains("size", skippedFile.Reason.ToLower());
    }

    [Fact]
    public async Task LargeRepo_FileTypeBreakdown_CorrectlyCounted()
    {
        // Arrange
        CreateTestFile("src/App.cs", 5 * 1024);
        CreateTestFile("src/Program.cs", 5 * 1024);
        CreateTestFile("src/Service.cs", 5 * 1024);
        CreateTestFile("web/index.ts", 5 * 1024);
        CreateTestFile("web/app.ts", 5 * 1024);
        CreateTestFile("config.json", 2 * 1024);

        var router = CreateRouter();

        // Act
        var estimate = await router.EstimateAnalysisAsync(_testDirectory);

        // Assert
        var totalFiles = estimate.DirectSendCount + estimate.RagChunkCount + estimate.SkippedCount;
        Assert.Equal(6, totalFiles);
        Assert.True(estimate.FileTypeBreakdown.ContainsKey(".cs"));
        Assert.Equal(3, estimate.FileTypeBreakdown[".cs"]);
        Assert.True(estimate.FileTypeBreakdown.ContainsKey(".ts"));
        Assert.Equal(2, estimate.FileTypeBreakdown[".ts"]);
        Assert.True(estimate.FileTypeBreakdown.ContainsKey(".json"));
        Assert.Equal(1, estimate.FileTypeBreakdown[".json"]);
    }

    [Fact]
    public async Task LargeRepo_NestedDirectories_AllFilesFound()
    {
        // Arrange - Create deeply nested structure
        CreateTestFile("src/app.cs", 5 * 1024);
        CreateTestFile("src/features/auth/login.cs", 5 * 1024);
        CreateTestFile("src/features/auth/logout.cs", 5 * 1024);
        CreateTestFile("src/features/dashboard/main.cs", 5 * 1024);
        CreateTestFile("src/lib/utils/helpers/strings.cs", 5 * 1024);

        var router = CreateRouter();

        // Act
        var estimate = await router.EstimateAnalysisAsync(_testDirectory);

        // Assert
        Assert.Equal(5, estimate.DirectSendCount);
    }

    [Fact]
    public async Task LargeRepo_EmptyFiles_ProcessedAsDirectSend()
    {
        // Arrange
        CreateTestFile("empty.cs", 0);
        CreateTestFile("normal.cs", 5 * 1024);

        var router = CreateRouter();

        // Act
        var estimate = await router.EstimateAnalysisAsync(_testDirectory);

        // Assert - Empty files should be DirectSend (size < threshold)
        Assert.Equal(2, estimate.DirectSendCount);
        Assert.Equal(0, estimate.SkippedCount);
    }

    [Fact]
    public async Task LargeRepo_100PlusFiles_NoMemoryIssues()
    {
        // Arrange - Create many files to stress test
        for (int i = 0; i < 500; i++)
        {
            CreateTestFile($"src/module{i % 10}/file{i}.cs", (i % 9) * 1024); // 0-8KB each
        }

        var router = CreateRouter();
        var beforeMemory = GC.GetTotalMemory(true);

        // Act
        var estimate = await router.EstimateAnalysisAsync(_testDirectory);

        var afterMemory = GC.GetTotalMemory(true);
        var memoryIncrease = (afterMemory - beforeMemory) / (1024.0 * 1024.0);

        // Assert
        Assert.Equal(500, estimate.DirectSendCount);
        Assert.True(memoryIncrease < 100, $"Memory increased by {memoryIncrease:F2}MB, should be < 100MB");
    }

    [Fact]
    public async Task LargeRepo_WithSkippedFiles_ReturnsDetailedReasons()
    {
        // Arrange - Create files that should be skipped
        CreateTestFile("too_big.cs", 500 * 1024);       // 500KB > 200KB - text file that's too large
        CreateTestFile("medium.cs", 50 * 1024);         // 50KB - should be RagChunks
        CreateTestFile("small.cs", 5 * 1024);           // 5KB - should be DirectSend

        var router = CreateRouter();

        // Act
        var estimate = await router.EstimateAnalysisAsync(_testDirectory);

        // Assert
        Assert.True(estimate.SkippedCount >= 1, "At least one file should be skipped");
        Assert.True(estimate.SkippedFiles.Count >= 1);

        // Verify each skipped file has a reason
        foreach (var skipped in estimate.SkippedFiles)
        {
            Assert.False(string.IsNullOrWhiteSpace(skipped.FilePath));
            Assert.False(string.IsNullOrWhiteSpace(skipped.Reason));
        }
    }

    [Fact]
    public void ComputeRoutingDecision_ForMultipleFiles_Consistent()
    {
        // Arrange
        var router = CreateRouter();

        var testCases = new[]
        {
            (size: 0L, expected: FileRoutingDecision.DirectSend),
            (size: 1024L, expected: FileRoutingDecision.DirectSend),
            (size: 10 * 1024L - 1, expected: FileRoutingDecision.DirectSend), // Just under 10KB
            (size: 10 * 1024L, expected: FileRoutingDecision.RagChunks),      // At 10KB
            (size: 100 * 1024L, expected: FileRoutingDecision.RagChunks),
            (size: 200 * 1024L, expected: FileRoutingDecision.RagChunks),     // At 200KB
            (size: 200 * 1024L + 1, expected: FileRoutingDecision.Skipped),   // Just over 200KB
            (size: 1024 * 1024L, expected: FileRoutingDecision.Skipped)
        };

        // Act & Assert
        foreach (var (size, expected) in testCases)
        {
            var (decision, _) = router.ComputeRoutingDecision("test.cs", size);
            Assert.Equal(expected, decision);
        }
    }

    [Fact]
    public async Task LargeRepo_AllowLargeFiles_ProcessesLargeFilesAsRag()
    {
        // Arrange
        CreateTestFile("small.cs", 5 * 1024);              // 5KB
        CreateTestFile("large.cs", 500 * 1024);            // 500KB - normally skipped

        var router = CreateRouter(allowLargeFiles: true);

        // Act
        var estimate = await router.EstimateAnalysisAsync(_testDirectory);

        // Assert - Large files should be processed as RagChunks when allowed
        Assert.Equal(1, estimate.DirectSendCount);  // small.cs
        Assert.Equal(1, estimate.RagChunkCount);    // large.cs (allowed)
        Assert.Equal(0, estimate.SkippedCount);
    }
}
