// =============================================================================
// AAR.Infrastructure - Services/PreflightService.cs
// Preflight validation service for repository size/cost estimation
// =============================================================================

using System.IO.Compression;
using AAR.Application.Configuration;
using AAR.Application.DTOs;
using AAR.Application.Interfaces;
using AAR.Domain.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AAR.Infrastructure.Services;

/// <summary>
/// Validates repositories before processing to estimate size, cost, and feasibility
/// </summary>
public sealed class PreflightService : IPreflightService
{
    private readonly IOrganizationQuotaRepository _quotaRepository;
    private readonly ScaleLimitsOptions _scaleLimits;
    private readonly ILogger<PreflightService> _logger;

    // File extensions to analyze for code content
    private static readonly HashSet<string> CodeExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".cs", ".csx", ".vb", ".fs", ".fsx",        // .NET
        ".java", ".kt", ".scala", ".groovy",         // JVM
        ".py", ".pyw", ".pyi",                       // Python
        ".js", ".jsx", ".ts", ".tsx", ".mjs", ".cjs", // JavaScript/TypeScript
        ".go", ".rs", ".c", ".cpp", ".cc", ".cxx", ".h", ".hpp", // Systems
        ".rb", ".php", ".swift", ".m", ".mm",        // Other
        ".sql", ".sh", ".bash", ".ps1", ".psm1",     // Scripts
        ".json", ".yaml", ".yml", ".xml", ".toml",   // Config
        ".md", ".txt", ".rst",                       // Docs
        ".html", ".htm", ".css", ".scss", ".less"    // Web
    };

    public PreflightService(
        IOrganizationQuotaRepository quotaRepository,
        IOptions<ScaleLimitsOptions> scaleLimits,
        ILogger<PreflightService> logger)
    {
        _quotaRepository = quotaRepository;
        _scaleLimits = scaleLimits.Value;
        _logger = logger;
    }

    public async Task<PreflightResponse> AnalyzeAsync(
        PreflightRequest request,
        string organizationId,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "Starting preflight analysis for organization {OrgId}, source: {Source}",
            organizationId,
            request.GitRepoUrl ?? request.FileName ?? "unknown");

        var limits = CreateLimits();

        // Basic validation
        if (string.IsNullOrEmpty(request.GitRepoUrl) && request.CompressedSizeBytes is null)
        {
            return CreateRejection(
                PreflightRejectionCodes.InvalidSource,
                "Either GitRepoUrl or file upload with size is required",
                limits);
        }

        // Estimate uncompressed size (compression ratio ~3-5x for code)
        var estimatedUncompressedSize = request.CompressedSizeBytes.HasValue
            ? request.CompressedSizeBytes.Value * 4  // Assume 4x compression ratio
            : 10 * 1024 * 1024L;  // Default estimate for git repos

        var estimatedFileCount = request.ExpectedFileCount
            ?? (int)(estimatedUncompressedSize / 5000);  // ~5KB average file

        // Check hard limits
        if (estimatedUncompressedSize > _scaleLimits.MaxRepoUncompressedSizeBytes)
        {
            return CreateRejection(
                PreflightRejectionCodes.RepoTooLarge,
                $"Estimated size {FormatBytes(estimatedUncompressedSize)} exceeds maximum {FormatBytes(_scaleLimits.MaxRepoUncompressedSizeBytes)}",
                limits);
        }

        if (estimatedFileCount > _scaleLimits.MaxFilesCount)
        {
            return CreateRejection(
                PreflightRejectionCodes.TooManyFiles,
                $"Estimated file count {estimatedFileCount:N0} exceeds maximum {_scaleLimits.MaxFilesCount:N0}",
                limits);
        }

        // Check organization quota
        var quota = await _quotaRepository.GetByOrganizationIdAsync(organizationId, cancellationToken);
        if (quota != null)
        {
            if (quota.IsSuspended)
            {
                return CreateRejection(
                    PreflightRejectionCodes.AccountSuspended,
                    "Account is suspended. Contact support.",
                    limits);
            }

            var estimatedCost = EstimateCost(estimatedUncompressedSize, estimatedFileCount);
            var remainingCredits = quota.GetRemainingCredits();

            if (estimatedCost > remainingCredits)
            {
                return CreateRejection(
                    PreflightRejectionCodes.InsufficientQuota,
                    $"Estimated cost {estimatedCost:F2} credits exceeds remaining {remainingCredits:F2} credits",
                    limits);
            }
        }

        // Calculate estimates
        var tokenCount = EstimateTokenCount(estimatedUncompressedSize);
        var cost = EstimateCost(estimatedUncompressedSize, estimatedFileCount);
        var requiresApproval = cost > _scaleLimits.MaxJobCostWithoutApproval;
        var canProcessSync = estimatedUncompressedSize <= _scaleLimits.SynchronousProcessingThresholdBytes
            && estimatedFileCount <= _scaleLimits.SynchronousProcessingMaxFiles;

        // Estimate processing time (rough: 1 file per 100ms + embedding overhead)
        var processingTimeSeconds = (int)(estimatedFileCount * 0.1 + tokenCount / 10000.0);

        var warnings = new List<string>();
        if (estimatedUncompressedSize > _scaleLimits.MaxRepoUncompressedSizeBytes * 0.8)
            warnings.Add("Repository is close to size limit");
        if (estimatedFileCount > _scaleLimits.MaxFilesCount * 0.8)
            warnings.Add("Repository is close to file count limit");
        if (requiresApproval)
            warnings.Add("This job requires approval due to estimated cost");

        _logger.LogInformation(
            "Preflight accepted: {Size}, {Files} files, {Tokens} tokens, {Cost:F4} credits",
            FormatBytes(estimatedUncompressedSize),
            estimatedFileCount,
            tokenCount,
            cost);

        return new PreflightResponse
        {
            IsAccepted = true,
            EstimatedUncompressedSizeBytes = estimatedUncompressedSize,
            EstimatedFileCount = estimatedFileCount,
            EstimatedLargestFileSizeBytes = Math.Min(estimatedUncompressedSize / 10, _scaleLimits.MaxSingleFileSizeBytes),
            EstimatedTokenCount = tokenCount,
            EstimatedCost = cost,
            RequiresApproval = requiresApproval,
            CanProcessSynchronously = canProcessSync,
            EstimatedProcessingTimeSeconds = processingTimeSeconds,
            Warnings = warnings,
            Limits = limits
        };
    }

    public async Task<PreflightResponse> AnalyzeZipAsync(
        Stream zipStream,
        string organizationId,
        CancellationToken cancellationToken = default)
    {
        var limits = CreateLimits();
        long totalSize = 0;
        int fileCount = 0;
        long largestFile = 0;
        long codeSize = 0;

        try
        {
            using var archive = new ZipArchive(zipStream, ZipArchiveMode.Read, leaveOpen: true);

            foreach (var entry in archive.Entries)
            {
                cancellationToken.ThrowIfCancellationRequested();

                // Skip directories
                if (string.IsNullOrEmpty(entry.Name))
                    continue;

                fileCount++;
                totalSize += entry.Length;
                largestFile = Math.Max(largestFile, entry.Length);

                // Track code file sizes for better token estimation
                var ext = Path.GetExtension(entry.Name);
                if (CodeExtensions.Contains(ext))
                    codeSize += entry.Length;

                // Early rejection checks
                if (fileCount > _scaleLimits.MaxFilesCount)
                {
                    return CreateRejection(
                        PreflightRejectionCodes.TooManyFiles,
                        $"Archive exceeds maximum {_scaleLimits.MaxFilesCount:N0} files",
                        limits);
                }

                if (entry.Length > _scaleLimits.MaxSingleFileSizeBytes)
                {
                    return CreateRejection(
                        PreflightRejectionCodes.FileTooLarge,
                        $"File '{entry.FullName}' ({FormatBytes(entry.Length)}) exceeds maximum {FormatBytes(_scaleLimits.MaxSingleFileSizeBytes)}",
                        limits);
                }

                if (totalSize > _scaleLimits.MaxRepoUncompressedSizeBytes)
                {
                    return CreateRejection(
                        PreflightRejectionCodes.RepoTooLarge,
                        $"Archive exceeds maximum size {FormatBytes(_scaleLimits.MaxRepoUncompressedSizeBytes)}",
                        limits);
                }
            }
        }
        catch (InvalidDataException ex)
        {
            _logger.LogWarning(ex, "Invalid zip file during preflight");
            return CreateRejection(
                PreflightRejectionCodes.InvalidSource,
                "Invalid or corrupted zip file",
                limits);
        }

        // Check quota
        var quota = await _quotaRepository.GetByOrganizationIdAsync(organizationId, cancellationToken);
        if (quota != null)
        {
            if (quota.IsSuspended)
            {
                return CreateRejection(
                    PreflightRejectionCodes.AccountSuspended,
                    "Account is suspended",
                    limits);
            }

            var estimatedCost = EstimateCost(totalSize, fileCount);
            var remainingCredits = quota.GetRemainingCredits();

            if (estimatedCost > remainingCredits)
            {
                return CreateRejection(
                    PreflightRejectionCodes.InsufficientQuota,
                    $"Estimated cost {estimatedCost:F2} credits exceeds remaining {remainingCredits:F2} credits",
                    limits);
            }
        }

        // Use code size for more accurate token estimation
        var effectiveSize = codeSize > 0 ? codeSize : totalSize;
        var tokenCount = EstimateTokenCount(effectiveSize);
        var cost = EstimateCost(totalSize, fileCount);
        var requiresApproval = cost > _scaleLimits.MaxJobCostWithoutApproval;
        var canProcessSync = totalSize <= _scaleLimits.SynchronousProcessingThresholdBytes
            && fileCount <= _scaleLimits.SynchronousProcessingMaxFiles;

        var processingTimeSeconds = (int)(fileCount * 0.1 + tokenCount / 10000.0);

        var warnings = new List<string>();
        if (totalSize > _scaleLimits.MaxRepoUncompressedSizeBytes * 0.8)
            warnings.Add("Repository is close to size limit");
        if (fileCount > _scaleLimits.MaxFilesCount * 0.8)
            warnings.Add("Repository is close to file count limit");
        if (requiresApproval)
            warnings.Add("This job requires approval due to estimated cost");

        _logger.LogInformation(
            "Preflight zip analysis complete: {Size}, {Files} files, {CodeSize} code, {Tokens} tokens",
            FormatBytes(totalSize),
            fileCount,
            FormatBytes(codeSize),
            tokenCount);

        return new PreflightResponse
        {
            IsAccepted = true,
            EstimatedUncompressedSizeBytes = totalSize,
            EstimatedFileCount = fileCount,
            EstimatedLargestFileSizeBytes = largestFile,
            EstimatedTokenCount = tokenCount,
            EstimatedCost = cost,
            RequiresApproval = requiresApproval,
            CanProcessSynchronously = canProcessSync,
            EstimatedProcessingTimeSeconds = processingTimeSeconds,
            Warnings = warnings,
            Limits = limits
        };
    }

    public decimal EstimateCost(long totalBytes, int fileCount)
    {
        // Estimate tokens from bytes
        var tokens = EstimateTokenCount(totalBytes);

        // Cost components:
        // 1. Embedding cost (all tokens)
        var embeddingCost = (tokens / 1000.0m) * _scaleLimits.EmbeddingCostPer1000Tokens;

        // 2. Reasoning cost (estimated 10% of tokens for summarization/analysis)
        var reasoningTokens = tokens * 0.1m;
        var reasoningCost = (reasoningTokens / 1000.0m) * _scaleLimits.ReasoningCostPer1000Tokens;

        // 3. Per-file overhead (for processing/chunking)
        var processingCost = fileCount * 0.001m;

        return embeddingCost + reasoningCost + processingCost;
    }

    public long EstimateTokenCount(long totalBytes)
    {
        // Source code typically has ~0.25 tokens per byte (accounting for whitespace/common words)
        return (long)(totalBytes * _scaleLimits.EstimatedTokensPerByte);
    }

    private PreflightLimits CreateLimits() => new()
    {
        MaxUncompressedSizeBytes = _scaleLimits.MaxRepoUncompressedSizeBytes,
        MaxSingleFileSizeBytes = _scaleLimits.MaxSingleFileSizeBytes,
        MaxFilesCount = _scaleLimits.MaxFilesCount,
        SynchronousThresholdBytes = _scaleLimits.SynchronousProcessingThresholdBytes,
        SynchronousMaxFiles = _scaleLimits.SynchronousProcessingMaxFiles,
        MaxCostWithoutApproval = _scaleLimits.MaxJobCostWithoutApproval
    };

    private static PreflightResponse CreateRejection(string code, string reason, PreflightLimits limits) => new()
    {
        IsAccepted = false,
        RejectionCode = code,
        RejectionReason = reason,
        Limits = limits
    };

    private static string FormatBytes(long bytes) => bytes switch
    {
        < 1024 => $"{bytes} B",
        < 1024 * 1024 => $"{bytes / 1024.0:F1} KB",
        < 1024 * 1024 * 1024 => $"{bytes / (1024.0 * 1024):F1} MB",
        _ => $"{bytes / (1024.0 * 1024 * 1024):F2} GB"
    };
}
