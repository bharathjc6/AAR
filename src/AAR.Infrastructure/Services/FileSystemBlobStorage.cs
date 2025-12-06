// =============================================================================
// AAR.Infrastructure - Services/FileSystemBlobStorage.cs
// Local file system implementation of blob storage for development
// =============================================================================

using AAR.Application.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.IO.Compression;

namespace AAR.Infrastructure.Services;

/// <summary>
/// File system-based blob storage for local development
/// </summary>
public class FileSystemBlobStorage : IBlobStorageService
{
    private readonly FileSystemStorageOptions _options;
    private readonly ILogger<FileSystemBlobStorage> _logger;
    private readonly string _basePath;

    public FileSystemBlobStorage(
        IOptions<FileSystemStorageOptions> options,
        ILogger<FileSystemBlobStorage> logger)
    {
        _options = options.Value;
        _logger = logger;
        _basePath = _options.BasePath ?? Path.Combine(Path.GetTempPath(), "aar-storage");
        
        // Ensure base directory exists
        Directory.CreateDirectory(_basePath);
        
        _logger.LogInformation("FileSystemBlobStorage initialized with base path: {BasePath}", _basePath);
    }

    /// <inheritdoc/>
    public async Task<string> UploadAsync(
        string containerName, 
        string blobName, 
        Stream content, 
        string contentType = "application/octet-stream",
        CancellationToken cancellationToken = default)
    {
        var containerPath = GetContainerPath(containerName);
        var blobPath = GetBlobPath(containerName, blobName);
        
        // Ensure container directory exists
        Directory.CreateDirectory(containerPath);
        
        // Ensure blob's parent directory exists
        var blobDir = Path.GetDirectoryName(blobPath);
        if (!string.IsNullOrEmpty(blobDir))
        {
            Directory.CreateDirectory(blobDir);
        }

        await using var fileStream = File.Create(blobPath);
        await content.CopyToAsync(fileStream, cancellationToken);

        _logger.LogDebug("Uploaded blob: {Container}/{BlobName}", containerName, blobName);
        
        return $"{containerName}/{blobName}";
    }

    /// <inheritdoc/>
    public async Task<Stream?> DownloadAsync(
        string containerName, 
        string blobName, 
        CancellationToken cancellationToken = default)
    {
        var blobPath = GetBlobPath(containerName, blobName);
        
        if (!File.Exists(blobPath))
        {
            _logger.LogWarning("Blob not found: {Container}/{BlobName}", containerName, blobName);
            return null;
        }

        // Return a memory stream with the file contents
        var memoryStream = new MemoryStream();
        await using var fileStream = File.OpenRead(blobPath);
        await fileStream.CopyToAsync(memoryStream, cancellationToken);
        memoryStream.Position = 0;
        
        return memoryStream;
    }

    /// <inheritdoc/>
    public async Task<byte[]?> DownloadBytesAsync(
        string containerName, 
        string blobName, 
        CancellationToken cancellationToken = default)
    {
        var blobPath = GetBlobPath(containerName, blobName);
        
        if (!File.Exists(blobPath))
        {
            return null;
        }

        return await File.ReadAllBytesAsync(blobPath, cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<string?> DownloadTextAsync(
        string containerName, 
        string blobName, 
        CancellationToken cancellationToken = default)
    {
        var blobPath = GetBlobPath(containerName, blobName);
        
        if (!File.Exists(blobPath))
        {
            return null;
        }

        return await File.ReadAllTextAsync(blobPath, cancellationToken);
    }

    /// <inheritdoc/>
    public Task<bool> ExistsAsync(
        string containerName, 
        string blobName, 
        CancellationToken cancellationToken = default)
    {
        var blobPath = GetBlobPath(containerName, blobName);
        return Task.FromResult(File.Exists(blobPath));
    }

    /// <inheritdoc/>
    public Task DeleteAsync(
        string containerName, 
        string blobName, 
        CancellationToken cancellationToken = default)
    {
        var blobPath = GetBlobPath(containerName, blobName);
        
        if (File.Exists(blobPath))
        {
            File.Delete(blobPath);
            _logger.LogDebug("Deleted blob: {Container}/{BlobName}", containerName, blobName);
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task DeleteByPrefixAsync(
        string containerName, 
        string prefix, 
        CancellationToken cancellationToken = default)
    {
        var containerPath = GetContainerPath(containerName);
        var prefixPath = Path.Combine(containerPath, prefix);
        
        if (Directory.Exists(prefixPath))
        {
            Directory.Delete(prefixPath, recursive: true);
            _logger.LogDebug("Deleted blobs with prefix: {Container}/{Prefix}", containerName, prefix);
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task<IReadOnlyList<string>> ListBlobsAsync(
        string containerName, 
        string? prefix = null, 
        CancellationToken cancellationToken = default)
    {
        var containerPath = GetContainerPath(containerName);
        
        if (!Directory.Exists(containerPath))
        {
            return Task.FromResult<IReadOnlyList<string>>(Array.Empty<string>());
        }

        var searchPath = string.IsNullOrEmpty(prefix) 
            ? containerPath 
            : Path.Combine(containerPath, prefix);

        if (!Directory.Exists(searchPath))
        {
            return Task.FromResult<IReadOnlyList<string>>(Array.Empty<string>());
        }

        var files = Directory.GetFiles(searchPath, "*", SearchOption.AllDirectories)
            .Select(f => Path.GetRelativePath(containerPath, f).Replace('\\', '/'))
            .ToList();

        return Task.FromResult<IReadOnlyList<string>>(files);
    }

    /// <inheritdoc/>
    public Task<string> GetDownloadUrlAsync(
        string containerName, 
        string blobName, 
        TimeSpan? expiry = null,
        CancellationToken cancellationToken = default)
    {
        // For local storage, return the file:// URL
        var blobPath = GetBlobPath(containerName, blobName);
        return Task.FromResult($"file://{blobPath}");
    }

    /// <inheritdoc/>
    public async Task<string> ExtractZipToTempAsync(
        string containerName, 
        string blobName, 
        CancellationToken cancellationToken = default)
    {
        var blobPath = GetBlobPath(containerName, blobName);
        
        if (!File.Exists(blobPath))
        {
            throw new FileNotFoundException($"Blob not found: {containerName}/{blobName}");
        }

        // Create temp directory for extraction
        var tempDir = Path.Combine(Path.GetTempPath(), "aar-extract", Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempDir);

        try
        {
            await Task.Run(() => ZipFile.ExtractToDirectory(blobPath, tempDir), cancellationToken);
            _logger.LogInformation("Extracted zip to: {TempDir}", tempDir);
            
            return tempDir;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error extracting zip: {Container}/{BlobName}", containerName, blobName);
            
            // Cleanup on error
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, recursive: true);
            }
            
            throw;
        }
    }

    private string GetContainerPath(string containerName)
    {
        // Sanitize container name
        var safeName = SanitizePath(containerName);
        return Path.Combine(_basePath, safeName);
    }

    private string GetBlobPath(string containerName, string blobName)
    {
        // Sanitize both parts
        var safeBlobName = SanitizePath(blobName);
        return Path.Combine(GetContainerPath(containerName), safeBlobName);
    }

    private static string SanitizePath(string path)
    {
        // Prevent path traversal attacks
        var normalized = path.Replace('\\', '/');
        
        if (normalized.Contains(".."))
        {
            throw new ArgumentException("Path traversal detected", nameof(path));
        }

        return normalized.Replace('/', Path.DirectorySeparatorChar);
    }
}

/// <summary>
/// Configuration options for file system storage
/// </summary>
public class FileSystemStorageOptions
{
    /// <summary>
    /// Base path for storing files (defaults to temp folder)
    /// </summary>
    public string? BasePath { get; set; }
}
