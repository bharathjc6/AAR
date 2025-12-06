// =============================================================================
// AAR.Application - Interfaces/IBlobStorageService.cs
// Abstraction for blob storage operations
// =============================================================================

namespace AAR.Application.Interfaces;

/// <summary>
/// Interface for blob storage operations
/// Implementations can target local filesystem or Azure Blob Storage
/// </summary>
public interface IBlobStorageService
{
    /// <summary>
    /// Uploads a file to blob storage
    /// </summary>
    /// <param name="containerName">Logical container/folder name</param>
    /// <param name="blobName">Name/path of the blob</param>
    /// <param name="content">File content stream</param>
    /// <param name="contentType">MIME type</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>URL or path to the uploaded blob</returns>
    Task<string> UploadAsync(
        string containerName, 
        string blobName, 
        Stream content, 
        string contentType = "application/octet-stream",
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Downloads a blob as a stream
    /// </summary>
    Task<Stream?> DownloadAsync(
        string containerName, 
        string blobName, 
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Downloads a blob as a byte array
    /// </summary>
    Task<byte[]?> DownloadBytesAsync(
        string containerName, 
        string blobName, 
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Downloads a blob as a string
    /// </summary>
    Task<string?> DownloadTextAsync(
        string containerName, 
        string blobName, 
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks if a blob exists
    /// </summary>
    Task<bool> ExistsAsync(
        string containerName, 
        string blobName, 
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes a blob
    /// </summary>
    Task DeleteAsync(
        string containerName, 
        string blobName, 
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes all blobs with a given prefix
    /// </summary>
    Task DeleteByPrefixAsync(
        string containerName, 
        string prefix, 
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists blobs with a given prefix
    /// </summary>
    Task<IReadOnlyList<string>> ListBlobsAsync(
        string containerName, 
        string? prefix = null, 
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a URL for downloading the blob (may be a SAS URL for Azure)
    /// </summary>
    Task<string> GetDownloadUrlAsync(
        string containerName, 
        string blobName, 
        TimeSpan? expiry = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Extracts a zip file to a temporary directory
    /// </summary>
    /// <param name="containerName">Container name</param>
    /// <param name="blobName">Blob name of the zip file</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Path to the extracted directory</returns>
    Task<string> ExtractZipToTempAsync(
        string containerName, 
        string blobName, 
        CancellationToken cancellationToken = default);
}
