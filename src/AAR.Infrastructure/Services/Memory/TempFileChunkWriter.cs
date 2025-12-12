// =============================================================================
// AAR.Infrastructure - Services/Memory/TempFileChunkWriter.cs
// Disk-backed chunk storage for memory-efficient processing
// =============================================================================

using AAR.Application.Configuration;
using AAR.Application.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AAR.Infrastructure.Services.Memory;

/// <summary>
/// Writes chunk content to temp files to avoid keeping large strings in memory.
/// </summary>
public class TempFileChunkWriter : ITempFileChunkWriter, IDisposable
{
    private readonly MemoryManagementOptions _options;
    private readonly ILogger<TempFileChunkWriter> _logger;
    private readonly string _baseTempPath;
    private readonly Dictionary<Guid, string> _jobPaths = new();
    private bool _disposed;

    public TempFileChunkWriter(
        IOptions<MemoryManagementOptions> options,
        ILogger<TempFileChunkWriter> logger)
    {
        _options = options.Value;
        _logger = logger;
        _baseTempPath = Path.Combine(Path.GetTempPath(), "aar-chunks");
        
        // Ensure base path exists
        Directory.CreateDirectory(_baseTempPath);
    }

    /// <inheritdoc/>
    public async Task<string> WriteChunkAsync(
        Guid jobId,
        string chunkId,
        string content,
        CancellationToken cancellationToken = default)
    {
        var jobPath = GetJobPath(jobId);
        var fileName = SanitizeFileName(chunkId) + ".chunk";
        var filePath = Path.Combine(jobPath, fileName);

        await File.WriteAllTextAsync(filePath, content, cancellationToken);

        _logger.LogDebug("Wrote chunk {ChunkId} to {FilePath} ({Size} bytes)",
            chunkId, filePath, content.Length);

        return filePath;
    }

    /// <inheritdoc/>
    public async Task<string> ReadChunkAsync(
        string tempFilePath,
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(tempFilePath))
        {
            throw new FileNotFoundException($"Chunk file not found: {tempFilePath}");
        }

        return await File.ReadAllTextAsync(tempFilePath, cancellationToken);
    }

    /// <inheritdoc/>
    public StreamReader GetChunkReader(string tempFilePath)
    {
        if (!File.Exists(tempFilePath))
        {
            throw new FileNotFoundException($"Chunk file not found: {tempFilePath}");
        }

        return new StreamReader(tempFilePath);
    }

    /// <inheritdoc/>
    public void CleanupJob(Guid jobId)
    {
        if (_jobPaths.TryGetValue(jobId, out var jobPath))
        {
            try
            {
                if (Directory.Exists(jobPath))
                {
                    Directory.Delete(jobPath, recursive: true);
                    _logger.LogInformation("Cleaned up temp folder for job {JobId}: {Path}", jobId, jobPath);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to cleanup temp folder for job {JobId}: {Path}", jobId, jobPath);
            }
            finally
            {
                _jobPaths.Remove(jobId);
            }
        }
    }

    /// <inheritdoc/>
    public long GetJobDiskUsage(Guid jobId)
    {
        if (!_jobPaths.TryGetValue(jobId, out var jobPath) || !Directory.Exists(jobPath))
        {
            return 0;
        }

        try
        {
            var files = Directory.GetFiles(jobPath, "*", SearchOption.AllDirectories);
            return files.Sum(f => new FileInfo(f).Length);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to calculate disk usage for job {JobId}", jobId);
            return 0;
        }
    }

    /// <inheritdoc/>
    public bool HasSufficientDiskSpace(long requiredBytes)
    {
        try
        {
            var drive = new DriveInfo(Path.GetPathRoot(_baseTempPath)!);
            var availableBytes = drive.AvailableFreeSpace;
            var requiredWithBuffer = requiredBytes + (_options.MinFreeDiskSpaceMB * 1024L * 1024L);

            if (availableBytes < requiredWithBuffer)
            {
                _logger.LogWarning(
                    "Insufficient disk space: available {AvailableMB} MB, required {RequiredMB} MB",
                    availableBytes / 1024 / 1024, requiredWithBuffer / 1024 / 1024);
                return false;
            }

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to check disk space");
            return true; // Assume OK if can't check
        }
    }

    private string GetJobPath(Guid jobId)
    {
        if (!_jobPaths.TryGetValue(jobId, out var jobPath))
        {
            jobPath = Path.Combine(_baseTempPath, jobId.ToString("N"));
            Directory.CreateDirectory(jobPath);
            _jobPaths[jobId] = jobPath;

            // Check disk quota
            var maxBytes = _options.MaxTempFolderSizeMB * 1024L * 1024L;
            if (!HasSufficientDiskSpace(maxBytes))
            {
                throw new InvalidOperationException(
                    $"Insufficient disk space for job. Required: {_options.MaxTempFolderSizeMB} MB");
            }
        }

        return jobPath;
    }

    private static string SanitizeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        return string.Join("_", name.Split(invalid, StringSplitOptions.RemoveEmptyEntries));
    }

    public void Dispose()
    {
        if (_disposed) return;

        // Cleanup all job folders
        foreach (var jobId in _jobPaths.Keys.ToList())
        {
            CleanupJob(jobId);
        }

        _disposed = true;
    }
}
