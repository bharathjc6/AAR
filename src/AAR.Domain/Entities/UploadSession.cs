// =============================================================================
// AAR.Domain - Entities/UploadSession.cs
// Entity for tracking resumable upload sessions
// =============================================================================

namespace AAR.Domain.Entities;

/// <summary>
/// Represents a resumable upload session for large files
/// </summary>
public class UploadSession : BaseEntity
{
    /// <summary>
    /// API Key ID that initiated the upload
    /// </summary>
    public Guid ApiKeyId { get; private set; }

    /// <summary>
    /// Project name for this upload
    /// </summary>
    public string ProjectName { get; private set; } = string.Empty;

    /// <summary>
    /// Optional project description
    /// </summary>
    public string? ProjectDescription { get; private set; }

    /// <summary>
    /// Original file name
    /// </summary>
    public string FileName { get; private set; } = string.Empty;

    /// <summary>
    /// Expected total file size in bytes
    /// </summary>
    public long TotalSizeBytes { get; private set; }

    /// <summary>
    /// Number of parts expected
    /// </summary>
    public int TotalParts { get; private set; }

    /// <summary>
    /// Parts that have been uploaded (comma-separated part numbers)
    /// </summary>
    public string UploadedParts { get; private set; } = string.Empty;

    /// <summary>
    /// Number of bytes uploaded so far
    /// </summary>
    public long BytesUploaded { get; private set; }

    /// <summary>
    /// Session status
    /// </summary>
    public UploadSessionStatus Status { get; private set; }

    /// <summary>
    /// Storage path for the parts
    /// </summary>
    public string StoragePath { get; private set; } = string.Empty;

    /// <summary>
    /// Session expiration time
    /// </summary>
    public DateTime ExpiresAt { get; private set; }

    /// <summary>
    /// Content hash (MD5) for integrity verification
    /// </summary>
    public string? ContentHash { get; private set; }

    /// <summary>
    /// Project ID once finalized
    /// </summary>
    public Guid? ProjectId { get; private set; }

    private UploadSession() { }

    public static UploadSession Create(
        Guid apiKeyId,
        string projectName,
        string? description,
        string fileName,
        long totalSize,
        int totalParts,
        string storagePath,
        int sessionTimeoutMinutes)
    {
        return new UploadSession
        {
            Id = Guid.NewGuid(),
            ApiKeyId = apiKeyId,
            ProjectName = projectName,
            ProjectDescription = description,
            FileName = fileName,
            TotalSizeBytes = totalSize,
            TotalParts = totalParts,
            UploadedParts = string.Empty,
            BytesUploaded = 0,
            Status = UploadSessionStatus.InProgress,
            StoragePath = storagePath,
            ExpiresAt = DateTime.UtcNow.AddMinutes(sessionTimeoutMinutes),
            CreatedAt = DateTime.UtcNow
        };
    }

    public void MarkPartUploaded(int partNumber, long partSize)
    {
        var parts = GetUploadedPartNumbers();
        if (!parts.Contains(partNumber))
        {
            parts.Add(partNumber);
            parts.Sort();
            UploadedParts = string.Join(",", parts);
            BytesUploaded += partSize;
        }
        SetUpdated();
    }

    public List<int> GetUploadedPartNumbers()
    {
        if (string.IsNullOrEmpty(UploadedParts))
            return new List<int>();
        
        return UploadedParts.Split(',')
            .Where(s => !string.IsNullOrEmpty(s))
            .Select(int.Parse)
            .ToList();
    }

    public List<int> GetMissingParts()
    {
        var uploaded = GetUploadedPartNumbers();
        return Enumerable.Range(1, TotalParts)
            .Where(p => !uploaded.Contains(p))
            .ToList();
    }

    public bool IsComplete => GetUploadedPartNumbers().Count == TotalParts;

    public bool IsExpired => DateTime.UtcNow > ExpiresAt;

    public void MarkFinalized(Guid projectId, string? contentHash)
    {
        Status = UploadSessionStatus.Finalized;
        ProjectId = projectId;
        ContentHash = contentHash;
        SetUpdated();
    }

    public void MarkFailed(string? reason = null)
    {
        Status = UploadSessionStatus.Failed;
        SetUpdated();
    }

    public void MarkExpired()
    {
        Status = UploadSessionStatus.Expired;
        SetUpdated();
    }

    public void ExtendExpiration(int additionalMinutes)
    {
        ExpiresAt = DateTime.UtcNow.AddMinutes(additionalMinutes);
        SetUpdated();
    }
}

/// <summary>
/// Upload session status values
/// </summary>
public enum UploadSessionStatus
{
    InProgress = 0,
    Finalized = 1,
    Failed = 2,
    Expired = 3,
    Cancelled = 4
}
