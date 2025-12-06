// =============================================================================
// AAR.Infrastructure - Services/AzureBlobStorage.cs
// Azure Blob Storage implementation (stub with TODO for real credentials)
// =============================================================================

using AAR.Application.Interfaces;
using Azure.Storage.Blobs;
using Azure.Storage.Sas;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.IO.Compression;

namespace AAR.Infrastructure.Services;

/// <summary>
/// Azure Blob Storage implementation
/// TODO: Configure AZURE_STORAGE_CONNECTION_STRING environment variable
/// </summary>
public class AzureBlobStorage : IBlobStorageService
{
    private readonly AzureBlobStorageOptions _options;
    private readonly BlobServiceClient? _blobServiceClient;
    private readonly ILogger<AzureBlobStorage> _logger;
    private readonly bool _isConfigured;

    public AzureBlobStorage(
        IOptions<AzureBlobStorageOptions> options,
        ILogger<AzureBlobStorage> logger)
    {
        _options = options.Value;
        _logger = logger;

        // TODO: Set this environment variable with your Azure Storage connection string
        var connectionString = _options.ConnectionString 
            ?? Environment.GetEnvironmentVariable("AZURE_STORAGE_CONNECTION_STRING");

        if (!string.IsNullOrEmpty(connectionString) && !connectionString.Contains("TODO"))
        {
            try
            {
                _blobServiceClient = new BlobServiceClient(connectionString);
                _isConfigured = true;
                _logger.LogInformation("AzureBlobStorage initialized successfully");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to initialize Azure Blob Storage client. Using mock mode.");
                _isConfigured = false;
            }
        }
        else
        {
            _logger.LogWarning("Azure Storage connection string not configured. Storage operations will fail.");
            _isConfigured = false;
        }
    }

    private void EnsureConfigured()
    {
        if (!_isConfigured || _blobServiceClient is null)
        {
            throw new InvalidOperationException(
                "Azure Blob Storage is not configured. " +
                "Set AZURE_STORAGE_CONNECTION_STRING environment variable or use FileSystemBlobStorage for local development.");
        }
    }

    private async Task<BlobContainerClient> GetContainerAsync(string containerName, CancellationToken cancellationToken)
    {
        EnsureConfigured();
        var containerClient = _blobServiceClient!.GetBlobContainerClient(containerName);
        await containerClient.CreateIfNotExistsAsync(cancellationToken: cancellationToken);
        return containerClient;
    }

    /// <inheritdoc/>
    public async Task<string> UploadAsync(
        string containerName, 
        string blobName, 
        Stream content, 
        string contentType = "application/octet-stream",
        CancellationToken cancellationToken = default)
    {
        var containerClient = await GetContainerAsync(containerName, cancellationToken);
        var blobClient = containerClient.GetBlobClient(blobName);
        
        await blobClient.UploadAsync(content, overwrite: true, cancellationToken);
        
        _logger.LogDebug("Uploaded blob to Azure: {Container}/{BlobName}", containerName, blobName);
        
        return blobClient.Uri.ToString();
    }

    /// <inheritdoc/>
    public async Task<Stream?> DownloadAsync(
        string containerName, 
        string blobName, 
        CancellationToken cancellationToken = default)
    {
        var containerClient = await GetContainerAsync(containerName, cancellationToken);
        var blobClient = containerClient.GetBlobClient(blobName);
        
        if (!await blobClient.ExistsAsync(cancellationToken))
        {
            return null;
        }

        var memoryStream = new MemoryStream();
        await blobClient.DownloadToAsync(memoryStream, cancellationToken);
        memoryStream.Position = 0;
        
        return memoryStream;
    }

    /// <inheritdoc/>
    public async Task<byte[]?> DownloadBytesAsync(
        string containerName, 
        string blobName, 
        CancellationToken cancellationToken = default)
    {
        await using var stream = await DownloadAsync(containerName, blobName, cancellationToken);
        if (stream is null) return null;
        
        using var ms = new MemoryStream();
        await stream.CopyToAsync(ms, cancellationToken);
        return ms.ToArray();
    }

    /// <inheritdoc/>
    public async Task<string?> DownloadTextAsync(
        string containerName, 
        string blobName, 
        CancellationToken cancellationToken = default)
    {
        var bytes = await DownloadBytesAsync(containerName, blobName, cancellationToken);
        return bytes is null ? null : System.Text.Encoding.UTF8.GetString(bytes);
    }

    /// <inheritdoc/>
    public async Task<bool> ExistsAsync(
        string containerName, 
        string blobName, 
        CancellationToken cancellationToken = default)
    {
        var containerClient = await GetContainerAsync(containerName, cancellationToken);
        var blobClient = containerClient.GetBlobClient(blobName);
        var response = await blobClient.ExistsAsync(cancellationToken);
        return response.Value;
    }

    /// <inheritdoc/>
    public async Task DeleteAsync(
        string containerName, 
        string blobName, 
        CancellationToken cancellationToken = default)
    {
        var containerClient = await GetContainerAsync(containerName, cancellationToken);
        var blobClient = containerClient.GetBlobClient(blobName);
        await blobClient.DeleteIfExistsAsync(cancellationToken: cancellationToken);
        
        _logger.LogDebug("Deleted blob from Azure: {Container}/{BlobName}", containerName, blobName);
    }

    /// <inheritdoc/>
    public async Task DeleteByPrefixAsync(
        string containerName, 
        string prefix, 
        CancellationToken cancellationToken = default)
    {
        var containerClient = await GetContainerAsync(containerName, cancellationToken);
        
        await foreach (var blobItem in containerClient.GetBlobsAsync(prefix: prefix, cancellationToken: cancellationToken))
        {
            var blobClient = containerClient.GetBlobClient(blobItem.Name);
            await blobClient.DeleteIfExistsAsync(cancellationToken: cancellationToken);
        }
        
        _logger.LogDebug("Deleted blobs with prefix from Azure: {Container}/{Prefix}", containerName, prefix);
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<string>> ListBlobsAsync(
        string containerName, 
        string? prefix = null, 
        CancellationToken cancellationToken = default)
    {
        var containerClient = await GetContainerAsync(containerName, cancellationToken);
        var blobs = new List<string>();
        
        await foreach (var blobItem in containerClient.GetBlobsAsync(prefix: prefix, cancellationToken: cancellationToken))
        {
            blobs.Add(blobItem.Name);
        }
        
        return blobs;
    }

    /// <inheritdoc/>
    public async Task<string> GetDownloadUrlAsync(
        string containerName, 
        string blobName, 
        TimeSpan? expiry = null,
        CancellationToken cancellationToken = default)
    {
        var containerClient = await GetContainerAsync(containerName, cancellationToken);
        var blobClient = containerClient.GetBlobClient(blobName);

        // Generate SAS token
        var sasBuilder = new BlobSasBuilder
        {
            BlobContainerName = containerName,
            BlobName = blobName,
            Resource = "b",
            ExpiresOn = DateTimeOffset.UtcNow.Add(expiry ?? TimeSpan.FromHours(1))
        };
        
        sasBuilder.SetPermissions(BlobSasPermissions.Read);
        
        var sasUri = blobClient.GenerateSasUri(sasBuilder);
        return sasUri.ToString();
    }

    /// <inheritdoc/>
    public async Task<string> ExtractZipToTempAsync(
        string containerName, 
        string blobName, 
        CancellationToken cancellationToken = default)
    {
        // Download to temp file first
        var tempZipPath = Path.Combine(Path.GetTempPath(), "aar-temp", $"{Guid.NewGuid()}.zip");
        var extractPath = Path.Combine(Path.GetTempPath(), "aar-extract", Guid.NewGuid().ToString());
        
        Directory.CreateDirectory(Path.GetDirectoryName(tempZipPath)!);
        Directory.CreateDirectory(extractPath);

        try
        {
            await using var stream = await DownloadAsync(containerName, blobName, cancellationToken);
            
            if (stream is null)
            {
                throw new FileNotFoundException($"Blob not found: {containerName}/{blobName}");
            }

            await using var fileStream = File.Create(tempZipPath);
            await stream.CopyToAsync(fileStream, cancellationToken);
            fileStream.Close();

            ZipFile.ExtractToDirectory(tempZipPath, extractPath);
            
            _logger.LogInformation("Extracted Azure blob zip to: {ExtractPath}", extractPath);
            
            return extractPath;
        }
        finally
        {
            // Cleanup temp zip file
            if (File.Exists(tempZipPath))
            {
                File.Delete(tempZipPath);
            }
        }
    }
}

/// <summary>
/// Configuration options for Azure Blob Storage
/// </summary>
public class AzureBlobStorageOptions
{
    /// <summary>
    /// Azure Storage connection string
    /// TODO: Set via environment variable AZURE_STORAGE_CONNECTION_STRING
    /// </summary>
    public string? ConnectionString { get; set; }
}
