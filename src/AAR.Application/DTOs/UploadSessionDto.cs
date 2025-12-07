// =============================================================================
// AAR.Application - DTOs/UploadSessionDto.cs
// DTOs for resumable upload sessions
// =============================================================================

namespace AAR.Application.DTOs;

/// <summary>
/// Request to initiate a resumable upload
/// </summary>
public record InitiateUploadRequest
{
    /// <summary>
    /// Project name
    /// </summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>
    /// Project description
    /// </summary>
    public string? Description { get; init; }

    /// <summary>
    /// Original file name
    /// </summary>
    public string FileName { get; init; } = string.Empty;

    /// <summary>
    /// Total file size in bytes
    /// </summary>
    public long TotalSizeBytes { get; init; }

    /// <summary>
    /// Number of parts to upload
    /// </summary>
    public int TotalParts { get; init; }

    /// <summary>
    /// Content hash (MD5) for integrity verification
    /// </summary>
    public string? ContentHash { get; init; }
}

/// <summary>
/// Response from initiating an upload
/// </summary>
public record InitiateUploadResponse
{
    /// <summary>
    /// Upload session ID
    /// </summary>
    public Guid SessionId { get; init; }

    /// <summary>
    /// URL pattern for uploading parts
    /// </summary>
    public string UploadUrl { get; init; } = string.Empty;

    /// <summary>
    /// URL to finalize the upload
    /// </summary>
    public string FinalizeUrl { get; init; } = string.Empty;

    /// <summary>
    /// Session expiration time
    /// </summary>
    public DateTime ExpiresAt { get; init; }

    /// <summary>
    /// Maximum part size allowed
    /// </summary>
    public long MaxPartSizeBytes { get; init; }

    /// <summary>
    /// Minimum part size required (except last part)
    /// </summary>
    public long MinPartSizeBytes { get; init; }
}

/// <summary>
/// Response from uploading a part
/// </summary>
public record UploadPartResponse
{
    /// <summary>
    /// Part number uploaded
    /// </summary>
    public int PartNumber { get; init; }

    /// <summary>
    /// Bytes uploaded for this part
    /// </summary>
    public long BytesUploaded { get; init; }

    /// <summary>
    /// Total bytes uploaded so far
    /// </summary>
    public long TotalBytesUploaded { get; init; }

    /// <summary>
    /// Parts still missing
    /// </summary>
    public List<int> MissingParts { get; init; } = new();

    /// <summary>
    /// Whether all parts are uploaded
    /// </summary>
    public bool IsComplete { get; init; }
}

/// <summary>
/// Response from finalizing an upload
/// </summary>
public record FinalizeUploadResponse
{
    /// <summary>
    /// Created project ID
    /// </summary>
    public Guid ProjectId { get; init; }

    /// <summary>
    /// Project name
    /// </summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>
    /// Project status
    /// </summary>
    public int Status { get; init; }

    /// <summary>
    /// Whether analysis was queued
    /// </summary>
    public bool AnalysisQueued { get; init; }

    /// <summary>
    /// Message about next steps
    /// </summary>
    public string Message { get; init; } = string.Empty;
}

/// <summary>
/// Upload session status response
/// </summary>
public record UploadSessionStatusResponse
{
    /// <summary>
    /// Session ID
    /// </summary>
    public Guid SessionId { get; init; }

    /// <summary>
    /// Session status
    /// </summary>
    public string Status { get; init; } = string.Empty;

    /// <summary>
    /// Total expected bytes
    /// </summary>
    public long TotalSizeBytes { get; init; }

    /// <summary>
    /// Bytes uploaded so far
    /// </summary>
    public long BytesUploaded { get; init; }

    /// <summary>
    /// Upload progress percentage
    /// </summary>
    public double ProgressPercent { get; init; }

    /// <summary>
    /// Parts uploaded
    /// </summary>
    public List<int> UploadedParts { get; init; } = new();

    /// <summary>
    /// Parts missing
    /// </summary>
    public List<int> MissingParts { get; init; } = new();

    /// <summary>
    /// Session expiration time
    /// </summary>
    public DateTime ExpiresAt { get; init; }

    /// <summary>
    /// Project ID if finalized
    /// </summary>
    public Guid? ProjectId { get; init; }
}
