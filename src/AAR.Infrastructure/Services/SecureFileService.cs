// =============================================================================
// AAR.Infrastructure - Services/SecureFileService.cs
// Production-ready secure file upload and extraction service
// =============================================================================

using System.IO.Compression;
using System.Security.Cryptography;
using AAR.Application.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AAR.Infrastructure.Services;

/// <summary>
/// Configuration for secure file operations
/// </summary>
public class SecureFileServiceOptions
{
    /// <summary>
    /// Base path for secure file storage
    /// </summary>
    public string BasePath { get; set; } = Path.Combine(Path.GetTempPath(), "aar-secure-storage");

    /// <summary>
    /// Maximum file size in bytes (default 100MB)
    /// </summary>
    public long MaxFileSizeBytes { get; set; } = 100 * 1024 * 1024;

    /// <summary>
    /// Maximum storage quota per user in bytes (default 1GB)
    /// </summary>
    public long MaxUserQuotaBytes { get; set; } = 1024 * 1024 * 1024;

    /// <summary>
    /// Maximum number of files in a ZIP archive
    /// </summary>
    public int MaxZipEntries { get; set; } = 10000;

    /// <summary>
    /// Maximum total extracted size (ZIP bomb protection)
    /// </summary>
    public long MaxExtractedSizeBytes { get; set; } = 500 * 1024 * 1024; // 500MB

    /// <summary>
    /// Maximum compression ratio allowed (ZIP bomb protection)
    /// </summary>
    public int MaxCompressionRatio { get; set; } = 100;

    /// <summary>
    /// File retention period in days
    /// </summary>
    public int FileRetentionDays { get; set; } = 30;
}

/// <summary>
/// Secure file service implementation with comprehensive validation and protection
/// </summary>
public class SecureFileService : ISecureFileService
{
    private readonly SecureFileServiceOptions _options;
    private readonly IVirusScanService _virusScanService;
    private readonly ILogger<SecureFileService> _logger;

    // Allowed file extensions for upload (whitelist approach)
    private static readonly HashSet<string> DefaultAllowedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".zip"
    };

    // Allowed MIME types
    private static readonly HashSet<string> DefaultAllowedMimeTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "application/zip",
        "application/x-zip-compressed",
        "application/x-zip",
        "application/octet-stream" // Often sent for binary files
    };

    // Disallowed file extensions in ZIP archives (executable/dangerous content)
    private static readonly HashSet<string> DisallowedArchiveExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        // Executables
        ".exe", ".dll", ".so", ".dylib", ".com", ".scr", ".pif",
        // Scripts that could be executed
        ".bat", ".cmd", ".ps1", ".psm1", ".psd1", ".vbs", ".vbe", ".js", ".jse", ".ws", ".wsf", ".wsc", ".wsh",
        // Shell scripts
        ".sh", ".bash", ".zsh", ".fish", ".csh", ".ksh",
        // Other dangerous
        ".msi", ".msp", ".mst", ".gadget", ".jar", ".hta", ".cpl", ".msc", ".inf",
        // Office macros
        ".docm", ".xlsm", ".pptm", ".dotm", ".xltm", ".potm", ".ppam", ".xlam", ".sldm",
        // Archives that could contain nested threats
        ".iso", ".img", ".vhd", ".vhdx"
    };

    // Maximum path length for extracted files
    private const int MaxPathLength = 260;

    public SecureFileService(
        IOptions<SecureFileServiceOptions> options,
        IVirusScanService virusScanService,
        ILogger<SecureFileService> logger)
    {
        _options = options.Value;
        _virusScanService = virusScanService;
        _logger = logger;

        // Ensure base directory exists with proper permissions
        EnsureSecureDirectory(_options.BasePath);
    }

    /// <inheritdoc/>
    public Task<FileValidationResult> ValidateFileAsync(
        string fileName,
        string? contentType,
        long fileSize,
        SecureFileOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var maxSize = options?.MaxFileSizeBytes ?? _options.MaxFileSizeBytes;
        var allowedExtensions = options?.AllowedExtensions ?? DefaultAllowedExtensions;
        var allowedMimeTypes = options?.AllowedMimeTypes ?? DefaultAllowedMimeTypes;

        // Validate file name
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return Task.FromResult(new FileValidationResult(false, "File name is required", "INVALID_FILENAME"));
        }

        // Check for path traversal in filename
        if (ContainsPathTraversal(fileName))
        {
            _logger.LogWarning("Path traversal attempt detected in filename: {FileName}", SanitizeForLog(fileName));
            return Task.FromResult(new FileValidationResult(false, "Invalid file name", "PATH_TRAVERSAL"));
        }

        // Validate file size
        if (fileSize <= 0)
        {
            return Task.FromResult(new FileValidationResult(false, "File is empty", "EMPTY_FILE"));
        }

        if (fileSize > maxSize)
        {
            return Task.FromResult(new FileValidationResult(
                false,
                $"File size exceeds maximum allowed ({maxSize / 1024 / 1024}MB)",
                "FILE_TOO_LARGE"));
        }

        // Validate extension
        var extension = Path.GetExtension(fileName);
        if (string.IsNullOrEmpty(extension) || !allowedExtensions.Contains(extension))
        {
            return Task.FromResult(new FileValidationResult(
                false,
                $"File type '{extension}' is not allowed",
                "INVALID_EXTENSION"));
        }

        // Validate MIME type if provided and validation is enabled
        if (options?.ValidateMimeType != false && !string.IsNullOrEmpty(contentType))
        {
            if (!allowedMimeTypes.Contains(contentType))
            {
                return Task.FromResult(new FileValidationResult(
                    false,
                    $"Content type '{contentType}' is not allowed",
                    "INVALID_CONTENT_TYPE"));
            }
        }

        return Task.FromResult(new FileValidationResult(true));
    }

    /// <inheritdoc/>
    public async Task<SecureUploadResult> UploadSecurelyAsync(
        Stream stream,
        string fileName,
        string? contentType,
        Guid userId,
        SecureFileOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        // Validate first
        var validation = await ValidateFileAsync(fileName, contentType, stream.Length, options, cancellationToken);
        if (!validation.IsValid)
        {
            return new SecureUploadResult(false, ErrorMessage: validation.ErrorMessage, ErrorCode: validation.ErrorCode);
        }

        // Check user quota
        var currentUsage = await GetUserStorageUsageAsync(userId, cancellationToken);
        var maxQuota = options?.MaxUserQuotaBytes ?? _options.MaxUserQuotaBytes;
        if (currentUsage + stream.Length > maxQuota)
        {
            _logger.LogWarning("User {UserId} exceeded storage quota. Current: {Current}, Requested: {Requested}, Max: {Max}",
                userId, currentUsage, stream.Length, maxQuota);
            return new SecureUploadResult(false, ErrorMessage: "Storage quota exceeded", ErrorCode: "QUOTA_EXCEEDED");
        }

        // Perform virus scan if enabled
        if (options?.PerformVirusScan != false && _virusScanService.IsAvailable)
        {
            var scanResult = await _virusScanService.ScanAsync(stream, fileName, cancellationToken);
            if (!scanResult.IsClean)
            {
                _logger.LogWarning("Virus detected in upload from user {UserId}: {ThreatName}", userId, scanResult.ThreatName);
                return new SecureUploadResult(false, ErrorMessage: "File failed security scan", ErrorCode: "VIRUS_DETECTED");
            }
            // Reset stream position after scan
            stream.Position = 0;
        }

        // Generate secure storage path with GUID name
        var secureFileName = $"{Guid.NewGuid()}{Path.GetExtension(fileName)}";
        var userPath = GetSecureUserPath(userId);
        var fullPath = Path.Combine(userPath, secureFileName);

        try
        {
            // Ensure user directory exists
            EnsureSecureDirectory(userPath);

            // Write file securely
            await using var fileStream = new FileStream(
                fullPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 81920,
                useAsync: true);

            await stream.CopyToAsync(fileStream, cancellationToken);

            _logger.LogInformation("Securely uploaded file for user {UserId}: {StoragePath} ({Size} bytes)",
                userId, secureFileName, stream.Length);

            return new SecureUploadResult(
                Success: true,
                StoragePath: Path.Combine(userId.ToString(), secureFileName),
                OriginalFileName: Path.GetFileName(fileName),
                FileSize: stream.Length,
                ContentType: contentType);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error uploading file for user {UserId}", userId);

            // Clean up on failure
            try { if (File.Exists(fullPath)) File.Delete(fullPath); } catch { /* ignore cleanup errors */ }

            return new SecureUploadResult(false, ErrorMessage: "Failed to store file", ErrorCode: "STORAGE_ERROR");
        }
    }

    /// <inheritdoc/>
    public async Task<SecureExtractionResult> ExtractZipSecurelyAsync(
        string storagePath,
        SecureFileOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var fullPath = GetFullPath(storagePath);

        if (!File.Exists(fullPath))
        {
            return new SecureExtractionResult(false, ErrorMessage: "File not found", ErrorCode: "FILE_NOT_FOUND");
        }

        // Create secure extraction directory
        var extractionId = Guid.NewGuid().ToString();
        var extractionPath = Path.Combine(_options.BasePath, "extractions", extractionId);

        try
        {
            EnsureSecureDirectory(extractionPath);

            var extractedFiles = new List<string>();
            long totalExtractedSize = 0;
            int entryCount = 0;

            await using var zipStream = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            var zipFileSize = zipStream.Length;

            using var archive = new ZipArchive(zipStream, ZipArchiveMode.Read);

            // Check entry count
            if (archive.Entries.Count > _options.MaxZipEntries)
            {
                _logger.LogWarning("ZIP archive exceeds maximum entry count: {Count} > {Max}",
                    archive.Entries.Count, _options.MaxZipEntries);
                return new SecureExtractionResult(
                    false,
                    ErrorMessage: $"Archive contains too many files (max {_options.MaxZipEntries})",
                    ErrorCode: "TOO_MANY_ENTRIES");
            }

            foreach (var entry in archive.Entries)
            {
                cancellationToken.ThrowIfCancellationRequested();

                // Skip directories
                if (string.IsNullOrEmpty(entry.Name))
                    continue;

                entryCount++;

                // Validate entry name for path traversal
                var validationResult = ValidateZipEntry(entry, extractionPath);
                if (!validationResult.IsValid)
                {
                    await CleanupExtractionAsync(extractionPath);
                    return new SecureExtractionResult(
                        false,
                        ErrorMessage: validationResult.ErrorMessage,
                        ErrorCode: validationResult.ErrorCode);
                }

                // Check for disallowed file types
                var extension = Path.GetExtension(entry.Name);
                if (DisallowedArchiveExtensions.Contains(extension))
                {
                    _logger.LogWarning("Disallowed file type in archive: {EntryName}", SanitizeForLog(entry.Name));
                    await CleanupExtractionAsync(extractionPath);
                    return new SecureExtractionResult(
                        false,
                        ErrorMessage: $"Archive contains disallowed file type: {extension}",
                        ErrorCode: "DISALLOWED_FILE_TYPE");
                }

                // Check extracted size (ZIP bomb protection)
                totalExtractedSize += entry.Length;
                if (totalExtractedSize > _options.MaxExtractedSizeBytes)
                {
                    _logger.LogWarning("ZIP extraction exceeds maximum size: {Size} > {Max}",
                        totalExtractedSize, _options.MaxExtractedSizeBytes);
                    await CleanupExtractionAsync(extractionPath);
                    return new SecureExtractionResult(
                        false,
                        ErrorMessage: "Extracted content exceeds maximum allowed size",
                        ErrorCode: "EXTRACTION_TOO_LARGE");
                }

                // Check compression ratio (ZIP bomb protection)
                if (entry.CompressedLength > 0)
                {
                    var ratio = entry.Length / entry.CompressedLength;
                    if (ratio > _options.MaxCompressionRatio)
                    {
                        _logger.LogWarning("Suspicious compression ratio detected: {Ratio}:1 for {Entry}",
                            ratio, SanitizeForLog(entry.Name));
                        await CleanupExtractionAsync(extractionPath);
                        return new SecureExtractionResult(
                            false,
                            ErrorMessage: "Suspicious compression ratio detected",
                            ErrorCode: "SUSPICIOUS_COMPRESSION");
                    }
                }

                // Extract safely
                var normalizedPath = NormalizePath(entry.FullName);
                var destinationPath = Path.GetFullPath(Path.Combine(extractionPath, normalizedPath));

                // Final path traversal check
                if (!destinationPath.StartsWith(extractionPath + Path.DirectorySeparatorChar))
                {
                    _logger.LogWarning("Path traversal attempt during extraction: {Entry} -> {Destination}",
                        SanitizeForLog(entry.FullName), destinationPath);
                    await CleanupExtractionAsync(extractionPath);
                    return new SecureExtractionResult(
                        false,
                        ErrorMessage: "Invalid file path in archive",
                        ErrorCode: "PATH_TRAVERSAL");
                }

                // Ensure destination directory exists
                var destDir = Path.GetDirectoryName(destinationPath);
                if (!string.IsNullOrEmpty(destDir))
                {
                    Directory.CreateDirectory(destDir);
                }

                // Extract with size validation
                await using var entryStream = entry.Open();
                await using var destStream = new FileStream(
                    destinationPath,
                    FileMode.CreateNew,
                    FileAccess.Write,
                    FileShare.None);

                var buffer = new byte[81920];
                long bytesWritten = 0;
                int bytesRead;

                while ((bytesRead = await entryStream.ReadAsync(buffer, cancellationToken)) > 0)
                {
                    bytesWritten += bytesRead;

                    // Real-time size check during extraction
                    if (bytesWritten > entry.Length * 1.1) // Allow 10% tolerance
                    {
                        _logger.LogWarning("Entry extraction exceeded declared size: {Entry}", SanitizeForLog(entry.Name));
                        await CleanupExtractionAsync(extractionPath);
                        return new SecureExtractionResult(
                            false,
                            ErrorMessage: "Archive entry size mismatch",
                            ErrorCode: "SIZE_MISMATCH");
                    }

                    await destStream.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken);
                }

                extractedFiles.Add(normalizedPath);
            }

            _logger.LogInformation("Successfully extracted {Count} files ({Size} bytes) to {Path}",
                entryCount, totalExtractedSize, extractionPath);

            return new SecureExtractionResult(
                Success: true,
                ExtractedPath: extractionPath,
                FileCount: entryCount,
                TotalSize: totalExtractedSize,
                ExtractedFiles: extractedFiles);
        }
        catch (InvalidDataException ex)
        {
            _logger.LogWarning(ex, "Invalid ZIP archive: {Path}", storagePath);
            await CleanupExtractionAsync(extractionPath);
            return new SecureExtractionResult(false, ErrorMessage: "Invalid or corrupted archive", ErrorCode: "INVALID_ARCHIVE");
        }
        catch (OperationCanceledException)
        {
            await CleanupExtractionAsync(extractionPath);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error extracting archive: {Path}", storagePath);
            await CleanupExtractionAsync(extractionPath);
            return new SecureExtractionResult(false, ErrorMessage: "Failed to extract archive", ErrorCode: "EXTRACTION_ERROR");
        }
    }

    /// <inheritdoc/>
    public Task<long> GetUserStorageUsageAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var userPath = GetSecureUserPath(userId);

        if (!Directory.Exists(userPath))
        {
            return Task.FromResult(0L);
        }

        try
        {
            var size = Directory.GetFiles(userPath, "*", SearchOption.AllDirectories)
                .Sum(f => new FileInfo(f).Length);
            return Task.FromResult(size);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error calculating storage usage for user {UserId}", userId);
            return Task.FromResult(0L);
        }
    }

    /// <inheritdoc/>
    public Task DeleteAsync(string storagePath, CancellationToken cancellationToken = default)
    {
        var fullPath = GetFullPath(storagePath);

        try
        {
            if (File.Exists(fullPath))
            {
                File.Delete(fullPath);
                _logger.LogDebug("Deleted file: {Path}", storagePath);
            }
            else if (Directory.Exists(fullPath))
            {
                Directory.Delete(fullPath, recursive: true);
                _logger.LogDebug("Deleted directory: {Path}", storagePath);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error deleting: {Path}", storagePath);
        }

        return Task.CompletedTask;
    }

    #region Private Helpers

    private FileValidationResult ValidateZipEntry(ZipArchiveEntry entry, string extractionBase)
    {
        var entryName = entry.FullName;

        // Check for empty name
        if (string.IsNullOrWhiteSpace(entryName))
        {
            return new FileValidationResult(false, "Empty entry name in archive", "INVALID_ENTRY_NAME");
        }

        // Check for path traversal patterns
        if (ContainsPathTraversal(entryName))
        {
            _logger.LogWarning("Path traversal in ZIP entry: {Entry}", SanitizeForLog(entryName));
            return new FileValidationResult(false, "Path traversal detected in archive", "PATH_TRAVERSAL");
        }

        // Check for absolute paths
        if (Path.IsPathRooted(entryName) || entryName.StartsWith("/") || entryName.StartsWith("\\"))
        {
            _logger.LogWarning("Absolute path in ZIP entry: {Entry}", SanitizeForLog(entryName));
            return new FileValidationResult(false, "Absolute path detected in archive", "ABSOLUTE_PATH");
        }

        // Check path length
        var fullPath = Path.Combine(extractionBase, NormalizePath(entryName));
        if (fullPath.Length > MaxPathLength)
        {
            return new FileValidationResult(false, "File path too long", "PATH_TOO_LONG");
        }

        return new FileValidationResult(true);
    }

    private static bool ContainsPathTraversal(string path)
    {
        if (string.IsNullOrEmpty(path))
            return false;

        // Normalize separators for checking
        var normalized = path.Replace('\\', '/');

        // Check for various path traversal patterns
        return normalized.Contains("../") ||
               normalized.Contains("..\\") ||
               normalized == ".." ||
               normalized.StartsWith("../") ||
               normalized.StartsWith("..\\") ||
               normalized.EndsWith("/..") ||
               normalized.EndsWith("\\..") ||
               normalized.Contains("/..") ||
               normalized.Contains("\\..") ||
               // Windows-specific patterns
               normalized.Contains("::") || // Alternate data streams
               normalized.Contains(":") && normalized.IndexOf(':') != 1; // Not a drive letter
    }

    private static string NormalizePath(string path)
    {
        // Replace backslashes with forward slashes and remove leading separators
        var normalized = path.Replace('\\', '/').TrimStart('/');

        // Remove any . or .. components
        var parts = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var cleanParts = new List<string>();

        foreach (var part in parts)
        {
            if (part == ".")
                continue;
            if (part == "..")
            {
                if (cleanParts.Count > 0)
                    cleanParts.RemoveAt(cleanParts.Count - 1);
                continue;
            }
            cleanParts.Add(part);
        }

        return string.Join(Path.DirectorySeparatorChar.ToString(), cleanParts);
    }

    private string GetSecureUserPath(Guid userId)
    {
        return Path.Combine(_options.BasePath, "uploads", userId.ToString());
    }

    private string GetFullPath(string storagePath)
    {
        // Validate storage path doesn't contain traversal
        if (ContainsPathTraversal(storagePath))
        {
            throw new ArgumentException("Invalid storage path", nameof(storagePath));
        }

        var fullPath = Path.GetFullPath(Path.Combine(_options.BasePath, "uploads", storagePath));

        // Ensure path is within base directory
        var uploadsPath = Path.GetFullPath(Path.Combine(_options.BasePath, "uploads"));
        if (!fullPath.StartsWith(uploadsPath + Path.DirectorySeparatorChar))
        {
            throw new ArgumentException("Path outside allowed directory", nameof(storagePath));
        }

        return fullPath;
    }

    private static void EnsureSecureDirectory(string path)
    {
        if (!Directory.Exists(path))
        {
            Directory.CreateDirectory(path);
            // TODO: Set restrictive permissions on Unix systems
            // On Windows, inherit from parent or use ACLs
        }
    }

    private static async Task CleanupExtractionAsync(string extractionPath)
    {
        await Task.Run(() =>
        {
            try
            {
                if (Directory.Exists(extractionPath))
                {
                    Directory.Delete(extractionPath, recursive: true);
                }
            }
            catch
            {
                // Ignore cleanup errors
            }
        });
    }

    private static string SanitizeForLog(string value)
    {
        // Truncate and remove potentially dangerous characters for logging
        if (string.IsNullOrEmpty(value))
            return "[empty]";

        var sanitized = value.Length > 100 ? value[..100] + "..." : value;
        return sanitized.Replace('\n', ' ').Replace('\r', ' ');
    }

    #endregion
}
