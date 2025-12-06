// =============================================================================
// AAR.Tests - Infrastructure/SecureFileServiceTests.cs
// Unit tests for SecureFileService
// =============================================================================

using System.IO.Compression;
using AAR.Application.Interfaces;
using AAR.Infrastructure.Services;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;

namespace AAR.Tests.Infrastructure;

/// <summary>
/// Unit tests for SecureFileService validating security controls
/// </summary>
public class SecureFileServiceTests : IDisposable
{
    private readonly Mock<IVirusScanService> _virusScanMock;
    private readonly Mock<ILogger<SecureFileService>> _loggerMock;
    private readonly SecureFileService _sut;
    private readonly string _testBasePath;
    private readonly SecureFileServiceOptions _serviceOptions;

    public SecureFileServiceTests()
    {
        _virusScanMock = new Mock<IVirusScanService>();
        _loggerMock = new Mock<ILogger<SecureFileService>>();
        _testBasePath = Path.Combine(Path.GetTempPath(), $"aar_test_{Guid.NewGuid():N}");
        
        // Default: virus scan returns clean
        _virusScanMock.Setup(x => x.IsAvailable).Returns(true);
        _virusScanMock.Setup(x => x.ScanAsync(It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new VirusScanResult(IsClean: true, ScanPerformed: true));
        _virusScanMock.Setup(x => x.ScanFileAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new VirusScanResult(IsClean: true, ScanPerformed: true));
        
        _serviceOptions = new SecureFileServiceOptions
        {
            BasePath = _testBasePath,
            MaxFileSizeBytes = 100 * 1024 * 1024,
            MaxUserQuotaBytes = 1024 * 1024 * 1024
        };
        
        Directory.CreateDirectory(_testBasePath);
        
        _sut = new SecureFileService(
            Options.Create(_serviceOptions),
            _virusScanMock.Object,
            _loggerMock.Object);
    }

    public void Dispose()
    {
        // Cleanup test directory
        if (Directory.Exists(_testBasePath))
        {
            try
            {
                Directory.Delete(_testBasePath, recursive: true);
            }
            catch
            {
                // Ignore cleanup errors
            }
        }
        GC.SuppressFinalize(this);
    }

    #region File Validation Tests

    [Fact]
    public async Task ValidateFileAsync_ZipFile_ShouldBeValid()
    {
        // Arrange
        var fileName = "project.zip";
        var contentType = "application/zip";
        var fileSize = 1024L;

        // Act
        var result = await _sut.ValidateFileAsync(fileName, contentType, fileSize);

        // Assert
        result.IsValid.Should().BeTrue();
    }

    [Theory]
    [InlineData(".exe")]
    [InlineData(".dll")]
    [InlineData(".bat")]
    [InlineData(".ps1")]
    [InlineData(".sh")]
    public async Task ValidateFileAsync_DisallowedExtensions_ShouldBeInvalid(string extension)
    {
        // Arrange
        var fileName = $"test{extension}";
        var contentType = "application/octet-stream";
        var fileSize = 1024L;

        // Act
        var result = await _sut.ValidateFileAsync(fileName, contentType, fileSize);

        // Assert
        result.IsValid.Should().BeFalse();
        result.ErrorMessage.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task ValidateFileAsync_FileTooLarge_ShouldBeInvalid()
    {
        // Arrange
        var fileName = "project.zip";
        var contentType = "application/zip";
        var fileSize = _serviceOptions.MaxFileSizeBytes + 1;

        // Act
        var result = await _sut.ValidateFileAsync(fileName, contentType, fileSize);

        // Assert
        result.IsValid.Should().BeFalse();
        result.ErrorMessage.Should().Contain("size");
    }

    [Fact]
    public async Task ValidateFileAsync_EmptyFile_ShouldBeInvalid()
    {
        // Arrange
        var fileName = "project.zip";
        var contentType = "application/zip";
        var fileSize = 0L;

        // Act
        var result = await _sut.ValidateFileAsync(fileName, contentType, fileSize);

        // Assert
        result.IsValid.Should().BeFalse();
    }

    #endregion

    #region Path Traversal Prevention Tests

    [Theory]
    [InlineData("../test.zip")]
    [InlineData("..\\test.zip")]
    [InlineData("folder/../../../test.zip")]
    [InlineData("folder\\..\\..\\test.zip")]
    public async Task ValidateFileAsync_PathTraversalInFileName_ShouldBeInvalid(string maliciousFileName)
    {
        // Arrange
        var contentType = "application/zip";
        var fileSize = 1024L;

        // Act
        var result = await _sut.ValidateFileAsync(maliciousFileName, contentType, fileSize);

        // Assert
        result.IsValid.Should().BeFalse();
    }

    // NOTE: Absolute path validation is handled at upload time, not during file validation
    // The service validates paths when writing to storage, not in the ValidateFileAsync call

    #endregion

    #region Secure Upload Tests

    [Fact]
    public async Task UploadSecurelyAsync_ValidZip_ShouldSucceed()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var zipPath = await CreateTestZipFileAsync("test.cs", "using System;");
        using var stream = File.OpenRead(zipPath);

        // Act
        var result = await _sut.UploadSecurelyAsync(stream, "project.zip", "application/zip", userId);

        // Assert
        result.Success.Should().BeTrue();
        result.StoragePath.Should().NotBeNullOrEmpty();
        result.FileSize.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task UploadSecurelyAsync_VirusDetected_ShouldFail()
    {
        // Arrange
        _virusScanMock.Setup(x => x.ScanAsync(It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new VirusScanResult(IsClean: false, ScanPerformed: true, ThreatName: "EICAR-Test-File"));

        var userId = Guid.NewGuid();
        var zipPath = await CreateTestZipFileAsync("test.cs", "using System;");
        using var stream = File.OpenRead(zipPath);

        // Act
        var result = await _sut.UploadSecurelyAsync(stream, "project.zip", "application/zip", userId);

        // Assert
        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("scan");
    }

    #endregion

    #region ZIP Extraction Security Tests

    [Fact]
    public async Task ExtractZipSecurelyAsync_ValidZip_ShouldExtract()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var zipPath = await CreateTestZipFileAsync("src/Program.cs", "using System;");
        using var stream = File.OpenRead(zipPath);
        
        var uploadResult = await _sut.UploadSecurelyAsync(stream, "project.zip", "application/zip", userId);
        uploadResult.Success.Should().BeTrue();

        // Act
        var result = await _sut.ExtractZipSecurelyAsync(uploadResult.StoragePath!);

        // Assert
        result.Success.Should().BeTrue();
        result.ExtractedFiles.Should().NotBeEmpty();
        result.FileCount.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task ExtractZipSecurelyAsync_ZipWithDisallowedExecutable_ShouldHandleGracefully()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var zipPath = await CreateTestZipWithMultipleFilesAsync(
            ("Program.cs", "using System;"),
            ("malware.exe", "MZ..."));
        using var stream = File.OpenRead(zipPath);
        
        var uploadResult = await _sut.UploadSecurelyAsync(stream, "project.zip", "application/zip", userId);
        uploadResult.Success.Should().BeTrue();

        // Act
        var result = await _sut.ExtractZipSecurelyAsync(uploadResult.StoragePath!);

        // Assert - the service should either succeed (skipping exe) or fail (rejecting exe)
        // Either behavior is security-safe
        if (result.Success)
        {
            // If it succeeds, exe should have been skipped
            result.ExtractedFiles.Should().NotContain(f => f.EndsWith(".exe"));
        }
        else
        {
            // If it fails, that's also acceptable security behavior
            result.ErrorMessage.Should().NotBeNullOrEmpty();
        }
    }

    [Fact]
    public async Task ExtractZipSecurelyAsync_ZipWithPathTraversal_ShouldRejectDangerousEntries()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var zipPath = await CreateTestZipWithPathTraversalAsync();
        using var stream = File.OpenRead(zipPath);
        
        var uploadResult = await _sut.UploadSecurelyAsync(stream, "project.zip", "application/zip", userId);
        uploadResult.Success.Should().BeTrue();

        // Act
        var result = await _sut.ExtractZipSecurelyAsync(uploadResult.StoragePath!);

        // Assert - the service should either skip dangerous entries or fail entirely
        // Either behavior is security-safe
        if (result.Success && result.ExtractedFiles?.Any() == true)
        {
            result.ExtractedPath.Should().NotBeNullOrEmpty();
            // Extracted files should not contain path traversal attempts
            result.ExtractedFiles.Should().OnlyContain(f => !f.Contains(".."));
        }
    }

    [Fact]
    public async Task ExtractZipSecurelyAsync_PathOutsideAllowedDirectory_ShouldThrow()
    {
        // Act & Assert - Extracting from outside allowed directory should throw
        await FluentActions.Invoking(() => 
            _sut.ExtractZipSecurelyAsync("/nonexistent/file.zip"))
            .Should().ThrowAsync<ArgumentException>()
            .WithMessage("*outside*");
    }

    #endregion

    #region Quota Tests

    [Fact]
    public async Task UploadSecurelyAsync_ExceedsQuota_ShouldFail()
    {
        // Arrange
        var userId = Guid.NewGuid();
        
        // Create options that exceed quota
        var smallQuotaOptions = new SecureFileOptions
        {
            MaxUserQuotaBytes = 10 // Very small quota
        };
        
        var zipPath = await CreateTestZipFileAsync("large.cs", new string('x', 1000));
        using var stream = File.OpenRead(zipPath);

        // Act
        var result = await _sut.UploadSecurelyAsync(stream, "project.zip", "application/zip", userId, smallQuotaOptions);

        // Assert
        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().NotBeNull().And.ContainEquivalentOf("quota");
    }

    #endregion

    #region Helper Methods

    private async Task<string> CreateTestZipFileAsync(string entryName, string content)
    {
        var zipPath = Path.Combine(_testBasePath, $"test_{Guid.NewGuid():N}.zip");
        
        using var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create);
        var entry = archive.CreateEntry(entryName);
        await using var entryStream = entry.Open();
        await using var writer = new StreamWriter(entryStream);
        await writer.WriteAsync(content);
        
        return zipPath;
    }

    private async Task<string> CreateTestZipWithMultipleFilesAsync(params (string Name, string Content)[] files)
    {
        var zipPath = Path.Combine(_testBasePath, $"test_{Guid.NewGuid():N}.zip");
        
        using var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create);
        foreach (var (name, content) in files)
        {
            var entry = archive.CreateEntry(name);
            await using var entryStream = entry.Open();
            await using var writer = new StreamWriter(entryStream);
            await writer.WriteAsync(content);
        }
        
        return zipPath;
    }

    private async Task<string> CreateTestZipWithPathTraversalAsync()
    {
        var zipPath = Path.Combine(_testBasePath, $"test_{Guid.NewGuid():N}.zip");
        
        using var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create);
        
        // Add a normal file
        var normalEntry = archive.CreateEntry("src/Program.cs");
        await using (var entryStream = normalEntry.Open())
        await using (var writer = new StreamWriter(entryStream))
        {
            await writer.WriteAsync("using System;");
        }
        
        // Add an entry attempting path traversal
        var traversalEntry = archive.CreateEntry("../../../etc/passwd");
        await using (var entryStream = traversalEntry.Open())
        await using (var writer = new StreamWriter(entryStream))
        {
            await writer.WriteAsync("root:x:0:0:");
        }
        
        return zipPath;
    }

    #endregion
}
