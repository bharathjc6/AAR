// =============================================================================
// AAR.Application - Interfaces/ISecureFileService.cs
// Secure file upload and extraction service abstraction
// =============================================================================

namespace AAR.Application.Interfaces;

/// <summary>
/// Result of a file validation operation
/// </summary>
public record FileValidationResult(bool IsValid, string? ErrorMessage = null, string? ErrorCode = null);

/// <summary>
/// Result of a secure file upload operation
/// </summary>
public record SecureUploadResult(
    bool Success,
    string? StoragePath = null,
    string? OriginalFileName = null,
    long FileSize = 0,
    string? ContentType = null,
    string? ErrorMessage = null,
    string? ErrorCode = null);

/// <summary>
/// Result of a secure ZIP extraction operation
/// </summary>
public record SecureExtractionResult(
    bool Success,
    string? ExtractedPath = null,
    int FileCount = 0,
    long TotalSize = 0,
    IReadOnlyList<string>? ExtractedFiles = null,
    string? ErrorMessage = null,
    string? ErrorCode = null);

/// <summary>
/// Options for secure file operations
/// </summary>
public record SecureFileOptions
{
    public long MaxFileSizeBytes { get; init; } = 100 * 1024 * 1024; // 100 MB default
    public long MaxUserQuotaBytes { get; init; } = 1024 * 1024 * 1024; // 1 GB default
    public bool PerformVirusScan { get; init; } = true;
    public bool ValidateMimeType { get; init; } = true;
    public IReadOnlySet<string>? AllowedExtensions { get; init; }
    public IReadOnlySet<string>? AllowedMimeTypes { get; init; }
}

/// <summary>
/// Secure file storage service that implements comprehensive validation,
/// sanitization, and safe extraction of uploaded files.
/// </summary>
public interface ISecureFileService
{
    /// <summary>
    /// Validates a file before upload
    /// </summary>
    /// <param name="fileName">Original file name</param>
    /// <param name="contentType">MIME type</param>
    /// <param name="fileSize">File size in bytes</param>
    /// <param name="options">Validation options (optional)</param>
    /// <returns>Validation result</returns>
    Task<FileValidationResult> ValidateFileAsync(
        string fileName,
        string? contentType,
        long fileSize,
        SecureFileOptions? options = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Securely uploads a file with validation
    /// </summary>
    /// <param name="stream">File content stream</param>
    /// <param name="fileName">Original file name</param>
    /// <param name="contentType">MIME type</param>
    /// <param name="userId">User/API key ID for quota tracking</param>
    /// <param name="options">Upload options</param>
    /// <returns>Upload result with storage path or error</returns>
    Task<SecureUploadResult> UploadSecurelyAsync(
        Stream stream,
        string fileName,
        string? contentType,
        Guid userId,
        SecureFileOptions? options = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Safely extracts a ZIP file preventing path traversal and rejecting disallowed content
    /// </summary>
    /// <param name="storagePath">Path to the ZIP file in storage</param>
    /// <param name="options">Extraction options</param>
    /// <returns>Extraction result with extracted path or error</returns>
    Task<SecureExtractionResult> ExtractZipSecurelyAsync(
        string storagePath,
        SecureFileOptions? options = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the current storage usage for a user
    /// </summary>
    /// <param name="userId">User/API key ID</param>
    /// <returns>Current usage in bytes</returns>
    Task<long> GetUserStorageUsageAsync(Guid userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes files and cleans up storage for a project
    /// </summary>
    /// <param name="storagePath">Storage path to delete</param>
    Task DeleteAsync(string storagePath, CancellationToken cancellationToken = default);
}
