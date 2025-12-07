// =============================================================================
// AAR.Application - DTOs/PreflightDto.cs
// DTOs for preflight analysis
// =============================================================================

namespace AAR.Application.DTOs;

/// <summary>
/// Request for preflight analysis
/// </summary>
public record PreflightRequest
{
    /// <summary>
    /// Git repository URL (if using git source)
    /// </summary>
    public string? GitRepoUrl { get; init; }

    /// <summary>
    /// Zip file name
    /// </summary>
    public string? FileName { get; init; }

    /// <summary>
    /// Compressed file size in bytes
    /// </summary>
    public long? CompressedSizeBytes { get; init; }

    /// <summary>
    /// Expected file count (if known from client)
    /// </summary>
    public int? ExpectedFileCount { get; init; }
}

/// <summary>
/// Response from preflight analysis
/// </summary>
public record PreflightResponse
{
    /// <summary>
    /// Whether the repository is accepted for processing
    /// </summary>
    public bool IsAccepted { get; init; }

    /// <summary>
    /// Rejection reason if not accepted
    /// </summary>
    public string? RejectionReason { get; init; }

    /// <summary>
    /// Rejection error code
    /// </summary>
    public string? RejectionCode { get; init; }

    /// <summary>
    /// Estimated uncompressed size in bytes
    /// </summary>
    public long EstimatedUncompressedSizeBytes { get; init; }

    /// <summary>
    /// Estimated file count
    /// </summary>
    public int EstimatedFileCount { get; init; }

    /// <summary>
    /// Estimated largest file size
    /// </summary>
    public long EstimatedLargestFileSizeBytes { get; init; }

    /// <summary>
    /// Estimated total token count
    /// </summary>
    public long EstimatedTokenCount { get; init; }

    /// <summary>
    /// Estimated cost in credits
    /// </summary>
    public decimal EstimatedCost { get; init; }

    /// <summary>
    /// Whether approval is required for this size
    /// </summary>
    public bool RequiresApproval { get; init; }

    /// <summary>
    /// Whether sync processing is available
    /// </summary>
    public bool CanProcessSynchronously { get; init; }

    /// <summary>
    /// Estimated processing time in seconds
    /// </summary>
    public int EstimatedProcessingTimeSeconds { get; init; }

    /// <summary>
    /// Warnings (non-blocking issues)
    /// </summary>
    public List<string> Warnings { get; init; } = new();

    /// <summary>
    /// Size limits that apply
    /// </summary>
    public PreflightLimits Limits { get; init; } = new();
}

/// <summary>
/// Current limits for preflight validation
/// </summary>
public record PreflightLimits
{
    public long MaxUncompressedSizeBytes { get; init; }
    public long MaxSingleFileSizeBytes { get; init; }
    public int MaxFilesCount { get; init; }
    public long SynchronousThresholdBytes { get; init; }
    public int SynchronousMaxFiles { get; init; }
    public decimal MaxCostWithoutApproval { get; init; }
}

/// <summary>
/// Preflight rejection codes
/// </summary>
public static class PreflightRejectionCodes
{
    public const string RepoTooLarge = "REPO_TOO_LARGE";
    public const string TooManyFiles = "TOO_MANY_FILES";
    public const string FileTooLarge = "FILE_TOO_LARGE";
    public const string InsufficientQuota = "INSUFFICIENT_QUOTA";
    public const string AccountSuspended = "ACCOUNT_SUSPENDED";
    public const string InvalidSource = "INVALID_SOURCE";
}
