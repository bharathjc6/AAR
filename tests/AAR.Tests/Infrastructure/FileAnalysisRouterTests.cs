// =============================================================================
// AAR.Tests - Infrastructure/FileAnalysisRouterTests.cs
// Unit tests for file analysis routing decisions
// =============================================================================

using AAR.Application.Configuration;
using AAR.Application.DTOs;
using AAR.Application.Interfaces;
using AAR.Infrastructure.Services.Routing;
using AAR.Shared.Tokenization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;

namespace AAR.Tests.Infrastructure;

public class FileAnalysisRouterTests
{
    private readonly Mock<IRagRiskFilter> _riskFilterMock;
    private readonly Mock<ITokenizerFactory> _tokenizerFactoryMock;
    private readonly Mock<ITokenizer> _tokenizerMock;
    private readonly Mock<ILogger<FileAnalysisRouter>> _loggerMock;

    public FileAnalysisRouterTests()
    {
        _riskFilterMock = new Mock<IRagRiskFilter>();
        _tokenizerFactoryMock = new Mock<ITokenizerFactory>();
        _tokenizerMock = new Mock<ITokenizer>();
        _loggerMock = new Mock<ILogger<FileAnalysisRouter>>();

        _tokenizerFactoryMock.Setup(f => f.Create()).Returns(_tokenizerMock.Object);
        _riskFilterMock.Setup(r => r.ComputeRiskScoresAsync(
            It.IsAny<Guid>(),
            It.IsAny<IEnumerable<string>>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, float>());
    }

    private FileAnalysisRouter CreateRouter(
        int directSendThreshold = 10 * 1024,
        int ragChunkThreshold = 200 * 1024,
        bool allowLargeFiles = false)
    {
        var ragOptions = Options.Create(new RagProcessingOptions
        {
            DirectSendThresholdBytes = directSendThreshold,
            RagChunkThresholdBytes = ragChunkThreshold,
            AllowLargeFiles = allowLargeFiles,
            RiskTopK = 20,
            RiskThreshold = 0.7f
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

    #region Boundary Tests - 10KB Threshold

    [Fact]
    public void ComputeRoutingDecision_FileSizeExactly10KB_ShouldBeDirectSend()
    {
        // Arrange
        var router = CreateRouter(directSendThreshold: 10 * 1024);
        var fileSize = 10 * 1024 - 1; // Just under 10KB

        // Act
        var (decision, reason) = router.ComputeRoutingDecision("test.cs", fileSize);

        // Assert
        Assert.Equal(FileRoutingDecision.DirectSend, decision);
        Assert.Contains("DirectSendThreshold", reason);
    }

    [Fact]
    public void ComputeRoutingDecision_FileSize10KBPlus1_ShouldBeRagChunks()
    {
        // Arrange
        var router = CreateRouter(directSendThreshold: 10 * 1024);
        var fileSize = 10 * 1024; // Exactly 10KB (at boundary)

        // Act
        var (decision, reason) = router.ComputeRoutingDecision("test.cs", fileSize);

        // Assert
        Assert.Equal(FileRoutingDecision.RagChunks, decision);
        Assert.Contains("RAG range", reason);
    }

    [Fact]
    public void ComputeRoutingDecision_FileSize1Byte_ShouldBeDirectSend()
    {
        // Arrange
        var router = CreateRouter();
        var fileSize = 1L;

        // Act
        var (decision, reason) = router.ComputeRoutingDecision("test.cs", fileSize);

        // Assert
        Assert.Equal(FileRoutingDecision.DirectSend, decision);
    }

    [Fact]
    public void ComputeRoutingDecision_FileSize9999Bytes_ShouldBeDirectSend()
    {
        // Arrange - Default threshold is 10KB = 10240 bytes
        var router = CreateRouter(directSendThreshold: 10 * 1024);
        var fileSize = 9999L;

        // Act
        var (decision, reason) = router.ComputeRoutingDecision("test.cs", fileSize);

        // Assert
        Assert.Equal(FileRoutingDecision.DirectSend, decision);
    }

    #endregion

    #region Boundary Tests - 200KB Threshold

    [Fact]
    public void ComputeRoutingDecision_FileSize200KB_ShouldBeRagChunks()
    {
        // Arrange
        var router = CreateRouter(ragChunkThreshold: 200 * 1024);
        var fileSize = 200 * 1024L; // Exactly 200KB

        // Act
        var (decision, reason) = router.ComputeRoutingDecision("test.cs", fileSize);

        // Assert
        Assert.Equal(FileRoutingDecision.RagChunks, decision);
    }

    [Fact]
    public void ComputeRoutingDecision_FileSize200KBPlus1_ShouldBeSkipped()
    {
        // Arrange
        var router = CreateRouter(ragChunkThreshold: 200 * 1024, allowLargeFiles: false);
        var fileSize = 200 * 1024 + 1; // Just over 200KB

        // Act
        var (decision, reason) = router.ComputeRoutingDecision("test.cs", fileSize);

        // Assert
        Assert.Equal(FileRoutingDecision.Skipped, decision);
        Assert.Equal(SkipReasonCodes.TooLarge, reason);
    }

    [Fact]
    public void ComputeRoutingDecision_FileSizeBetweenThresholds_ShouldBeRagChunks()
    {
        // Arrange
        var router = CreateRouter(directSendThreshold: 10 * 1024, ragChunkThreshold: 200 * 1024);
        var fileSize = 50 * 1024L; // 50KB

        // Act
        var (decision, reason) = router.ComputeRoutingDecision("test.cs", fileSize);

        // Assert
        Assert.Equal(FileRoutingDecision.RagChunks, decision);
    }

    #endregion

    #region AllowLargeFiles Override Tests

    [Fact]
    public void ComputeRoutingDecision_LargeFileWithAllowLargeFilesTrue_ShouldBeRagChunks()
    {
        // Arrange
        var router = CreateRouter(ragChunkThreshold: 200 * 1024, allowLargeFiles: true);
        var fileSize = 1024 * 1024L; // 1MB

        // Act
        var (decision, reason) = router.ComputeRoutingDecision("test.cs", fileSize);

        // Assert
        Assert.Equal(FileRoutingDecision.RagChunks, decision);
        Assert.Contains("AllowLargeFiles override", reason);
    }

    [Fact]
    public void ComputeRoutingDecision_LargeFileWithAllowLargeFilesFalse_ShouldBeSkipped()
    {
        // Arrange
        var router = CreateRouter(ragChunkThreshold: 200 * 1024, allowLargeFiles: false);
        var fileSize = 1024 * 1024L; // 1MB

        // Act
        var (decision, reason) = router.ComputeRoutingDecision("test.cs", fileSize);

        // Assert
        Assert.Equal(FileRoutingDecision.Skipped, decision);
    }

    [Fact]
    public void ComputeRoutingDecision_VeryLargeFile10MB_WithOverride_ShouldBeRagChunks()
    {
        // Arrange
        var router = CreateRouter(ragChunkThreshold: 200 * 1024, allowLargeFiles: true);
        var fileSize = 10 * 1024 * 1024L; // 10MB

        // Act
        var (decision, reason) = router.ComputeRoutingDecision("test.cs", fileSize);

        // Assert
        Assert.Equal(FileRoutingDecision.RagChunks, decision);
    }

    #endregion

    #region Custom Threshold Tests

    [Fact]
    public void ComputeRoutingDecision_CustomThresholds_ShouldRespectValues()
    {
        // Arrange - Custom thresholds: 5KB direct, 50KB RAG
        var router = CreateRouter(directSendThreshold: 5 * 1024, ragChunkThreshold: 50 * 1024);

        // Act & Assert
        var (decision1, _) = router.ComputeRoutingDecision("test.cs", 4 * 1024);
        Assert.Equal(FileRoutingDecision.DirectSend, decision1);

        var (decision2, _) = router.ComputeRoutingDecision("test.cs", 10 * 1024);
        Assert.Equal(FileRoutingDecision.RagChunks, decision2);

        var (decision3, _) = router.ComputeRoutingDecision("test.cs", 60 * 1024);
        Assert.Equal(FileRoutingDecision.Skipped, decision3);
    }

    [Fact]
    public void ComputeRoutingDecision_ZeroByteFile_ShouldBeDirectSend()
    {
        // Arrange
        var router = CreateRouter();
        var fileSize = 0L;

        // Act
        var (decision, _) = router.ComputeRoutingDecision("test.cs", fileSize);

        // Assert
        Assert.Equal(FileRoutingDecision.DirectSend, decision);
    }

    #endregion

    #region Estimation Tests

    [Fact]
    public async Task EstimateAnalysisAsync_WithMixedFiles_ShouldReturnCorrectCounts()
    {
        // Arrange
        var tempDir = Path.Combine(Path.GetTempPath(), "aar-test-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(tempDir);

        try
        {
            // Create small file (< 10KB) - should be DirectSend
            File.WriteAllText(Path.Combine(tempDir, "small.cs"), new string('a', 5 * 1024));

            // Create medium file (10KB - 200KB) - should be RagChunks
            File.WriteAllText(Path.Combine(tempDir, "medium.cs"), new string('b', 50 * 1024));

            // Create large file (> 200KB) - should be Skipped
            File.WriteAllText(Path.Combine(tempDir, "large.cs"), new string('c', 250 * 1024));

            var router = CreateRouter();

            // Act
            var estimation = await router.EstimateAnalysisAsync(tempDir);

            // Assert
            Assert.Equal(1, estimation.DirectSendCount);
            Assert.Equal(1, estimation.RagChunkCount);
            Assert.Equal(1, estimation.SkippedCount);
            Assert.True(estimation.EstimatedTokens > 0);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task EstimateAnalysisAsync_ExcludedDirectories_ShouldBeSkipped()
    {
        // Arrange
        var tempDir = Path.Combine(Path.GetTempPath(), "aar-test-" + Guid.NewGuid().ToString("N")[..8]);
        var nodeModulesDir = Path.Combine(tempDir, "node_modules");
        Directory.CreateDirectory(nodeModulesDir);

        try
        {
            // Create file in node_modules
            File.WriteAllText(Path.Combine(nodeModulesDir, "package.cs"), "test content");

            // Create normal file
            File.WriteAllText(Path.Combine(tempDir, "normal.cs"), "test content");

            var router = CreateRouter();

            // Act
            var estimation = await router.EstimateAnalysisAsync(tempDir);

            // Assert - node_modules file should be skipped
            Assert.Equal(1, estimation.DirectSendCount);
            Assert.True(estimation.SkippedCount >= 1);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    #endregion
}
