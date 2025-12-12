// AAR.Tests - Mocks/MockBlobStorageService.cs
// In-memory blob storage for testing

using AAR.Application.Interfaces;
using System.Collections.Concurrent;
using System.IO.Compression;
using System.Text;

namespace AAR.Tests.Mocks;

/// <summary>
/// In-memory blob storage implementation for testing.
/// </summary>
public class MockBlobStorageService : IBlobStorageService
{
    private readonly ConcurrentDictionary<string, BlobEntry> _blobs = new();
    private readonly ConcurrentBag<BlobOperation> _operations = new();

    public IReadOnlyCollection<BlobOperation> Operations => _operations.ToArray();
    public int BlobCount => _blobs.Count;
    public void Reset() { _blobs.Clear(); _operations.Clear(); }

    public Task<string> UploadAsync(string containerName, string blobName, Stream content, 
        string contentType = "application/octet-stream", CancellationToken cancellationToken = default)
    {
        var key = $"{containerName}/{blobName}";
        using var ms = new MemoryStream();
        content.CopyTo(ms);
        _blobs[key] = new BlobEntry { Container = containerName, Name = blobName, Content = ms.ToArray(), ContentType = contentType };
        _operations.Add(new BlobOperation { Type = "Upload", Container = containerName, BlobName = blobName, Size = ms.Length });
        return Task.FromResult(key);
    }

    public Task<Stream?> DownloadAsync(string containerName, string blobName, CancellationToken cancellationToken = default)
    {
        var key = $"{containerName}/{blobName}";
        _operations.Add(new BlobOperation { Type = "Download", Container = containerName, BlobName = blobName });
        if (_blobs.TryGetValue(key, out var entry))
            return Task.FromResult<Stream?>(new MemoryStream(entry.Content));
        return Task.FromResult<Stream?>(null);
    }

    public Task<byte[]?> DownloadBytesAsync(string containerName, string blobName, CancellationToken cancellationToken = default)
    {
        var key = $"{containerName}/{blobName}";
        if (_blobs.TryGetValue(key, out var entry))
            return Task.FromResult<byte[]?>(entry.Content);
        return Task.FromResult<byte[]?>(null);
    }

    public Task<string?> DownloadTextAsync(string containerName, string blobName, CancellationToken cancellationToken = default)
    {
        var key = $"{containerName}/{blobName}";
        if (_blobs.TryGetValue(key, out var entry))
            return Task.FromResult<string?>(Encoding.UTF8.GetString(entry.Content));
        return Task.FromResult<string?>(null);
    }

    public Task<bool> ExistsAsync(string containerName, string blobName, CancellationToken cancellationToken = default)
    {
        var key = $"{containerName}/{blobName}";
        return Task.FromResult(_blobs.ContainsKey(key));
    }

    public Task DeleteAsync(string containerName, string blobName, CancellationToken cancellationToken = default)
    {
        var key = $"{containerName}/{blobName}";
        _blobs.TryRemove(key, out _);
        _operations.Add(new BlobOperation { Type = "Delete", Container = containerName, BlobName = blobName });
        return Task.CompletedTask;
    }

    public Task DeleteByPrefixAsync(string containerName, string prefix, CancellationToken cancellationToken = default)
    {
        var keysToRemove = _blobs.Keys.Where(k => k.StartsWith($"{containerName}/{prefix}")).ToList();
        foreach (var key in keysToRemove) _blobs.TryRemove(key, out _);
        _operations.Add(new BlobOperation { Type = "DeleteByPrefix", Container = containerName, BlobName = prefix });
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<string>> ListBlobsAsync(string containerName, string? prefix = null, CancellationToken cancellationToken = default)
    {
        var blobs = _blobs.Values
            .Where(b => b.Container == containerName)
            .Where(b => prefix == null || b.Name.StartsWith(prefix))
            .Select(b => b.Name)
            .ToList();
        return Task.FromResult<IReadOnlyList<string>>(blobs);
    }

    public Task<string> GetDownloadUrlAsync(string containerName, string blobName, TimeSpan? expiry = null, CancellationToken cancellationToken = default)
    {
        return Task.FromResult($"https://mock.blob.core.windows.net/{containerName}/{blobName}");
    }

    public Task<string> ExtractZipToTempAsync(string containerName, string blobName, CancellationToken cancellationToken = default)
    {
        var key = $"{containerName}/{blobName}";
        if (!_blobs.TryGetValue(key, out var entry))
            throw new FileNotFoundException($"Blob not found: {key}");

        var tempDir = Path.Combine(Path.GetTempPath(), $"aar_test_{Guid.NewGuid()}");
        Directory.CreateDirectory(tempDir);

        using var ms = new MemoryStream(entry.Content);
        using var archive = new ZipArchive(ms, ZipArchiveMode.Read);
        archive.ExtractToDirectory(tempDir);

        return Task.FromResult(tempDir);
    }
}

public record BlobEntry
{
    public required string Container { get; init; }
    public required string Name { get; init; }
    public required byte[] Content { get; init; }
    public required string ContentType { get; init; }
}

public record BlobOperation
{
    public required string Type { get; init; }
    public required string Container { get; init; }
    public required string BlobName { get; init; }
    public long Size { get; init; }
}
