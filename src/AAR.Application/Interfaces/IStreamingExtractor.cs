// =============================================================================
// AAR.Application - Interfaces/IStreamingExtractor.cs
// Interface for streaming zip extraction
// =============================================================================

namespace AAR.Application.Interfaces;

/// <summary>
/// Represents metadata about an extracted file
/// </summary>
public record ExtractedFileInfo
{
    public string RelativePath { get; init; } = string.Empty;
    public string FullPath { get; init; } = string.Empty;
    public long SizeBytes { get; init; }
    public string? ContentHash { get; init; }
    public DateTime LastModified { get; init; }
    public int FileIndex { get; init; }
}

/// <summary>
/// Progress callback for streaming extraction
/// </summary>
public delegate Task ExtractionProgressCallback(int filesExtracted, int totalFiles, string currentFile);

/// <summary>
/// Service for streaming extraction of archives
/// </summary>
public interface IStreamingExtractor
{
    /// <summary>
    /// Extracts a zip file using streaming (low memory)
    /// </summary>
    /// <param name="zipStream">Source zip stream</param>
    /// <param name="outputDirectory">Target directory</param>
    /// <param name="maxFileSize">Maximum individual file size to extract</param>
    /// <param name="maxTotalFiles">Maximum files to extract</param>
    /// <param name="progressCallback">Optional progress callback</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Extracted file information</returns>
    IAsyncEnumerable<ExtractedFileInfo> ExtractStreamingAsync(
        Stream zipStream,
        string outputDirectory,
        long maxFileSize,
        int maxTotalFiles,
        ExtractionProgressCallback? progressCallback = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Validates a zip file without full extraction
    /// </summary>
    Task<ZipValidationResult> ValidateZipAsync(
        Stream zipStream,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Assembles uploaded parts into a single file
    /// </summary>
    Task<string> AssemblePartsAsync(
        string partsDirectory,
        int totalParts,
        string outputPath,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Result of zip validation
/// </summary>
public record ZipValidationResult
{
    public bool IsValid { get; init; }
    public int FileCount { get; init; }
    public long TotalUncompressedSize { get; init; }
    public long LargestFileSize { get; init; }
    public string? LargestFileName { get; init; }
    public List<string> Errors { get; init; } = new();
    public List<string> Warnings { get; init; } = new();
    public Dictionary<string, int> FileExtensions { get; init; } = new();
}
