// =============================================================================
// AAR.Infrastructure - Services/StreamingZipExtractor.cs
// Streaming zip extraction with memory-efficient processing
// =============================================================================

using System.IO.Compression;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using AAR.Application.Interfaces;
using Microsoft.Extensions.Logging;

namespace AAR.Infrastructure.Services;

/// <summary>
/// Memory-efficient streaming zip extractor
/// Processes files one at a time without loading entire archive into memory
/// </summary>
public sealed class StreamingZipExtractor : IStreamingExtractor
{
    private readonly ILogger<StreamingZipExtractor> _logger;

    // Extensions to skip during extraction
    private static readonly HashSet<string> SkipExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".exe", ".dll", ".so", ".dylib",    // Binaries
        ".zip", ".tar", ".gz", ".rar",       // Archives
        ".png", ".jpg", ".jpeg", ".gif", ".bmp", ".ico", ".svg", // Images
        ".mp3", ".mp4", ".avi", ".mov", ".wav", // Media
        ".pdf", ".doc", ".docx", ".ppt",     // Documents
        ".node_modules",                      // Dependencies marker
        ".lock"                              // Lock files
    };

    public StreamingZipExtractor(ILogger<StreamingZipExtractor> logger)
    {
        _logger = logger;
    }

    public async IAsyncEnumerable<ExtractedFileInfo> ExtractStreamingAsync(
        Stream zipStream,
        string outputDirectory,
        long maxFileSize,
        int maxTotalFiles,
        ExtractionProgressCallback? progressCallback = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        // Ensure output directory exists
        Directory.CreateDirectory(outputDirectory);

        using var archive = new ZipArchive(zipStream, ZipArchiveMode.Read, leaveOpen: true);
        
        var extractedCount = 0;
        var totalEntries = archive.Entries.Count;
        var fileIndex = 0;

        foreach (var entry in archive.Entries)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // Skip directories
            if (string.IsNullOrEmpty(entry.Name))
                continue;

            // Check limits
            if (extractedCount >= maxTotalFiles)
            {
                _logger.LogWarning(
                    "Reached maximum file count ({Max}), stopping extraction",
                    maxTotalFiles);
                yield break;
            }

            // Skip files that are too large
            if (entry.Length > maxFileSize)
            {
                _logger.LogWarning(
                    "Skipping file {Name}: size {Size} exceeds maximum {Max}",
                    entry.FullName, entry.Length, maxFileSize);
                continue;
            }

            // Skip certain extensions
            var extension = Path.GetExtension(entry.Name);
            if (SkipExtensions.Contains(extension))
            {
                _logger.LogDebug("Skipping binary/media file: {Name}", entry.FullName);
                continue;
            }

            // Skip node_modules and other common dependency folders
            if (ShouldSkipPath(entry.FullName))
            {
                _logger.LogDebug("Skipping dependency path: {Name}", entry.FullName);
                continue;
            }

            // Sanitize path to prevent zip slip attacks
            var sanitizedPath = SanitizePath(entry.FullName);
            var fullPath = Path.Combine(outputDirectory, sanitizedPath);

            // Ensure the path is within output directory
            var normalizedOutput = Path.GetFullPath(outputDirectory);
            var normalizedFull = Path.GetFullPath(fullPath);
            if (!normalizedFull.StartsWith(normalizedOutput, StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogWarning("Skipping path traversal attempt: {Name}", entry.FullName);
                continue;
            }

            // Create directory structure
            var fileDir = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrEmpty(fileDir))
                Directory.CreateDirectory(fileDir);

            // Extract file
            string? contentHash = null;
            bool extractionSucceeded = false;
            try
            {
                await using var entryStream = entry.Open();
                await using var fileStream = File.Create(fullPath);

                using var hashStream = new HashStream(fileStream);
                await entryStream.CopyToAsync(hashStream, cancellationToken);

                contentHash = hashStream.GetHashString();
                extractedCount++;
                fileIndex++;

                _logger.LogDebug(
                    "Extracted [{Index}/{Total}]: {Name} ({Size} bytes)",
                    extractedCount, totalEntries, entry.FullName, entry.Length);

                // Report progress
                if (progressCallback != null)
                {
                    await progressCallback(extractedCount, totalEntries, entry.FullName);
                }

                extractionSucceeded = true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to extract {Name}", entry.FullName);
                // Continue with next file
            }

            if (extractionSucceeded)
            {
                yield return new ExtractedFileInfo
                {
                    RelativePath = sanitizedPath,
                    FullPath = fullPath,
                    SizeBytes = entry.Length,
                    ContentHash = contentHash,
                    LastModified = entry.LastWriteTime.DateTime,
                    FileIndex = fileIndex
                };
            }
        }

        _logger.LogInformation(
            "Extraction complete: {Extracted} files from {Total} entries",
            extractedCount, totalEntries);
    }

    public async Task<ZipValidationResult> ValidateZipAsync(
        Stream zipStream,
        CancellationToken cancellationToken = default)
    {
        var result = new ZipValidationResult
        {
            Errors = new List<string>(),
            Warnings = new List<string>(),
            FileExtensions = new Dictionary<string, int>()
        };

        try
        {
            using var archive = new ZipArchive(zipStream, ZipArchiveMode.Read, leaveOpen: true);

            long totalSize = 0;
            long largestFile = 0;
            string? largestFileName = null;
            var fileCount = 0;

            foreach (var entry in archive.Entries)
            {
                cancellationToken.ThrowIfCancellationRequested();

                // Skip directories
                if (string.IsNullOrEmpty(entry.Name))
                    continue;

                fileCount++;
                totalSize += entry.Length;

                if (entry.Length > largestFile)
                {
                    largestFile = entry.Length;
                    largestFileName = entry.FullName;
                }

                // Track extensions
                var ext = Path.GetExtension(entry.Name).ToLowerInvariant();
                if (!string.IsNullOrEmpty(ext))
                {
                    result.FileExtensions[ext] = result.FileExtensions.GetValueOrDefault(ext, 0) + 1;
                }

                // Check for suspicious patterns
                if (entry.FullName.Contains(".."))
                    result.Warnings.Add($"Suspicious path: {entry.FullName}");
            }

            return result with
            {
                IsValid = result.Errors.Count == 0,
                FileCount = fileCount,
                TotalUncompressedSize = totalSize,
                LargestFileSize = largestFile,
                LargestFileName = largestFileName
            };
        }
        catch (InvalidDataException ex)
        {
            result.Errors.Add($"Invalid zip file: {ex.Message}");
            return result with { IsValid = false };
        }
    }

    public async Task<string> AssemblePartsAsync(
        string partsDirectory,
        int totalParts,
        string outputPath,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "Assembling {Parts} parts from {Dir} to {Output}",
            totalParts, partsDirectory, outputPath);

        var outputDir = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrEmpty(outputDir))
            Directory.CreateDirectory(outputDir);

        await using var outputStream = File.Create(outputPath);

        for (int i = 1; i <= totalParts; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var partPath = Path.Combine(partsDirectory, $"part-{i:D5}");
            
            if (!File.Exists(partPath))
                throw new FileNotFoundException($"Missing part {i}", partPath);

            await using var partStream = File.OpenRead(partPath);
            await partStream.CopyToAsync(outputStream, cancellationToken);

            _logger.LogDebug("Assembled part {Part}/{Total}", i, totalParts);
        }

        var fileInfo = new FileInfo(outputPath);
        _logger.LogInformation(
            "Assembly complete: {Size} bytes",
            fileInfo.Length);

        return outputPath;
    }

    private static string SanitizePath(string path)
    {
        // Replace backslashes with forward slashes
        path = path.Replace('\\', '/');

        // Remove leading slashes
        path = path.TrimStart('/');

        // Remove any path traversal attempts
        var segments = path.Split('/');
        var sanitized = segments.Where(s => s != ".." && s != ".").ToArray();

        return string.Join(Path.DirectorySeparatorChar.ToString(), sanitized);
    }

    private static bool ShouldSkipPath(string path)
    {
        var normalizedPath = path.Replace('\\', '/').ToLowerInvariant();
        
        return normalizedPath.Contains("/node_modules/") ||
               normalizedPath.Contains("/vendor/") ||
               normalizedPath.Contains("/packages/") ||
               normalizedPath.Contains("/.git/") ||
               normalizedPath.Contains("/bin/") ||
               normalizedPath.Contains("/obj/") ||
               normalizedPath.Contains("/__pycache__/") ||
               normalizedPath.Contains("/.vs/") ||
               normalizedPath.Contains("/.idea/");
    }

    /// <summary>
    /// Stream wrapper that computes hash while writing
    /// </summary>
    private sealed class HashStream : Stream
    {
        private readonly Stream _inner;
        private readonly SHA256 _sha256;
        private byte[]? _hash;

        public HashStream(Stream inner)
        {
            _inner = inner;
            _sha256 = SHA256.Create();
        }

        public string GetHashString()
        {
            if (_hash == null)
            {
                _sha256.TransformFinalBlock([], 0, 0);
                _hash = _sha256.Hash ?? [];
            }
            return Convert.ToHexString(_hash).ToLowerInvariant();
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            _sha256.TransformBlock(buffer, offset, count, null, 0);
            _inner.Write(buffer, offset, count);
        }

        public override async Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            _sha256.TransformBlock(buffer, offset, count, null, 0);
            await _inner.WriteAsync(buffer, offset, count, cancellationToken);
        }

        public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            var array = buffer.ToArray();
            _sha256.TransformBlock(array, 0, array.Length, null, 0);
            await _inner.WriteAsync(buffer, cancellationToken);
        }

        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => _inner.Length;
        public override long Position { get => _inner.Position; set => _inner.Position = value; }
        public override void Flush() => _inner.Flush();
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => _inner.SetLength(value);

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _sha256.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
