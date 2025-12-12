// =============================================================================
// AAR.Infrastructure - Services/Routing/FileAnalysisRouter.cs
// Routes files to appropriate analysis strategies based on size and content
// =============================================================================

using AAR.Application.Configuration;
using AAR.Application.DTOs;
using AAR.Application.Interfaces;
using AAR.Shared.Tokenization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AAR.Infrastructure.Services.Routing;

/// <summary>
/// Routes files to appropriate analysis strategies based on size thresholds.
/// </summary>
public class FileAnalysisRouter : IFileAnalysisRouter
{
    private readonly RagProcessingOptions _ragOptions;
    private readonly JobApprovalOptions _approvalOptions;
    private readonly ScaleLimitsOptions _scaleLimits;
    private readonly IRagRiskFilter _riskFilter;
    private readonly ITokenizer _tokenizer;
    private readonly ILogger<FileAnalysisRouter> _logger;

    private static readonly HashSet<string> SourceExtensions =
    [
        ".cs", ".ts", ".tsx", ".js", ".jsx", ".py", ".java", ".go", ".rs", 
        ".cpp", ".c", ".h", ".hpp", ".rb", ".php", ".swift", ".kt", ".scala", 
        ".vue", ".svelte", ".razor", ".cshtml", ".fs", ".fsx", ".vb", ".lua",
        ".r", ".jl", ".dart", ".elm", ".clj", ".ex", ".exs", ".erl", ".hrl"
    ];

    private static readonly HashSet<string> ExcludedDirectories =
    [
        "node_modules", "bin", "obj", ".git", ".vs", ".idea", ".vscode",
        "packages", "dist", "build", "__pycache__", ".venv", "venv",
        "coverage", ".nyc_output", "TestResults", ".nuget", "vendor",
        ".gradle", "target", "out", ".next", ".cache"
    ];

    private static readonly HashSet<string> BinaryExtensions =
    [
        ".exe", ".dll", ".so", ".dylib", ".pdb", ".obj", ".o", ".a",
        ".zip", ".tar", ".gz", ".rar", ".7z", ".jar", ".war", ".ear",
        ".png", ".jpg", ".jpeg", ".gif", ".bmp", ".ico", ".svg", ".webp",
        ".mp3", ".mp4", ".avi", ".mov", ".mkv", ".wav", ".flac",
        ".pdf", ".doc", ".docx", ".xls", ".xlsx", ".ppt", ".pptx",
        ".woff", ".woff2", ".ttf", ".eot", ".otf"
    ];

    public FileAnalysisRouter(
        IOptions<RagProcessingOptions> ragOptions,
        IOptions<JobApprovalOptions> approvalOptions,
        IOptions<ScaleLimitsOptions> scaleLimits,
        IRagRiskFilter riskFilter,
        ITokenizerFactory tokenizerFactory,
        ILogger<FileAnalysisRouter> logger)
    {
        _ragOptions = ragOptions.Value;
        _approvalOptions = approvalOptions.Value;
        _scaleLimits = scaleLimits.Value;
        _riskFilter = riskFilter;
        _tokenizer = tokenizerFactory.Create();
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task<ProjectAnalysisPlan> CreateAnalysisPlanAsync(
        Guid projectId,
        string workingDirectory,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Creating analysis plan for project {ProjectId}", projectId);

        var files = new List<FileAnalysisPlan>();
        var fileTypeBreakdown = new Dictionary<string, int>();

        // Enumerate all files
        foreach (var filePath in Directory.EnumerateFiles(workingDirectory, "*.*", SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var relativePath = Path.GetRelativePath(workingDirectory, filePath);
            var extension = Path.GetExtension(filePath).ToLowerInvariant();

            // Track file type breakdown
            if (!string.IsNullOrEmpty(extension))
            {
                fileTypeBreakdown.TryGetValue(extension, out var count);
                fileTypeBreakdown[extension] = count + 1;
            }

            // Check for excluded paths
            if (IsExcludedPath(relativePath))
            {
                files.Add(new FileAnalysisPlan
                {
                    FilePath = relativePath,
                    FullPath = filePath,
                    Decision = FileRoutingDecision.Skipped,
                    DecisionReason = SkipReasonCodes.ExcludedPath
                });
                continue;
            }

            // Check for binary files
            if (BinaryExtensions.Contains(extension))
            {
                files.Add(new FileAnalysisPlan
                {
                    FilePath = relativePath,
                    FullPath = filePath,
                    Decision = FileRoutingDecision.Skipped,
                    DecisionReason = SkipReasonCodes.BinaryFile
                });
                continue;
            }

            // Check for non-source files
            if (!SourceExtensions.Contains(extension))
            {
                // Still process config files and similar
                if (!IsConfigFile(relativePath))
                {
                    files.Add(new FileAnalysisPlan
                    {
                        FilePath = relativePath,
                        FullPath = filePath,
                        Decision = FileRoutingDecision.Skipped,
                        DecisionReason = SkipReasonCodes.ExcludedPath
                    });
                    continue;
                }
            }

            // Get file size
            long fileSize;
            try
            {
                var fileInfo = new FileInfo(filePath);
                fileSize = fileInfo.Length;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to get file info for {FilePath}", relativePath);
                files.Add(new FileAnalysisPlan
                {
                    FilePath = relativePath,
                    FullPath = filePath,
                    Decision = FileRoutingDecision.Skipped,
                    DecisionReason = SkipReasonCodes.ReadError
                });
                continue;
            }

            // Compute routing decision
            var (decision, reason) = ComputeRoutingDecision(relativePath, fileSize);

            // Estimate tokens
            var estimatedTokens = EstimateTokens(fileSize);

            files.Add(new FileAnalysisPlan
            {
                FilePath = relativePath,
                FullPath = filePath,
                FileSizeBytes = fileSize,
                Decision = decision,
                DecisionReason = reason,
                EstimatedTokens = estimatedTokens,
                Language = DetectLanguage(extension)
            });
        }

        _logger.LogInformation(
            "Initial routing: {DirectSend} direct, {RagChunk} RAG, {Skipped} skipped",
            files.Count(f => f.Decision == FileRoutingDecision.DirectSend),
            files.Count(f => f.Decision == FileRoutingDecision.RagChunks),
            files.Count(f => f.Decision == FileRoutingDecision.Skipped));

        // Compute risk scores for non-skipped files
        var processableFiles = files
            .Where(f => f.Decision != FileRoutingDecision.Skipped)
            .Select(f => f.FilePath)
            .ToList();

        if (processableFiles.Count > 0)
        {
            try
            {
                var riskScores = await _riskFilter.ComputeRiskScoresAsync(
                    projectId, processableFiles, cancellationToken);

                // Update files with risk scores
                for (var i = 0; i < files.Count; i++)
                {
                    var file = files[i];
                    if (riskScores.TryGetValue(file.FilePath, out var score))
                    {
                        files[i] = file with
                        {
                            RiskScore = score,
                            IsHighRisk = score >= _ragOptions.RiskThreshold
                        };
                    }
                }

                var highRiskCount = files.Count(f => f.IsHighRisk);
                _logger.LogInformation("Identified {HighRiskCount} high-risk files", highRiskCount);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to compute risk scores, proceeding without prioritization");
            }
        }

        return new ProjectAnalysisPlan
        {
            ProjectId = projectId,
            WorkingDirectory = workingDirectory,
            Files = files
        };
    }

    /// <inheritdoc/>
    public (FileRoutingDecision Decision, string Reason) ComputeRoutingDecision(
        string filePath, 
        long fileSizeBytes)
    {
        // Small files: direct send
        if (fileSizeBytes < _ragOptions.DirectSendThresholdBytes)
        {
            return (FileRoutingDecision.DirectSend, 
                $"File size {fileSizeBytes} bytes < DirectSendThreshold {_ragOptions.DirectSendThresholdBytes}");
        }

        // Medium files: RAG chunking
        if (fileSizeBytes <= _ragOptions.RagChunkThresholdBytes)
        {
            return (FileRoutingDecision.RagChunks, 
                $"File size {fileSizeBytes} bytes within RAG range");
        }

        // Large files: check enterprise override
        if (_ragOptions.AllowLargeFiles)
        {
            return (FileRoutingDecision.RagChunks, 
                $"Large file ({fileSizeBytes} bytes) allowed via AllowLargeFiles override");
        }

        // Skip large files
        return (FileRoutingDecision.Skipped, SkipReasonCodes.TooLarge);
    }

    /// <inheritdoc/>
    public async Task<AnalysisEstimation> EstimateAnalysisAsync(
        string workingDirectory,
        CancellationToken cancellationToken = default)
    {
        var directSendCount = 0;
        var ragChunkCount = 0;
        var skippedCount = 0;
        long estimatedTokens = 0;
        var warnings = new List<string>();
        var skippedFiles = new List<SkippedFileInfo>();
        var fileTypeBreakdown = new Dictionary<string, int>();

        foreach (var filePath in Directory.EnumerateFiles(workingDirectory, "*.*", SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var relativePath = Path.GetRelativePath(workingDirectory, filePath);
            var extension = Path.GetExtension(filePath).ToLowerInvariant();

            // Track file type
            if (!string.IsNullOrEmpty(extension))
            {
                fileTypeBreakdown.TryGetValue(extension, out var count);
                fileTypeBreakdown[extension] = count + 1;
            }

            // Skip excluded paths
            if (IsExcludedPath(relativePath))
            {
                skippedCount++;
                continue;
            }

            // Skip binary files
            if (BinaryExtensions.Contains(extension))
            {
                skippedCount++;
                continue;
            }

            // Skip non-source files (unless config)
            if (!SourceExtensions.Contains(extension) && !IsConfigFile(relativePath))
            {
                skippedCount++;
                continue;
            }

            long fileSize;
            try
            {
                fileSize = new FileInfo(filePath).Length;
            }
            catch
            {
                skippedCount++;
                continue;
            }

            var (decision, reason) = ComputeRoutingDecision(relativePath, fileSize);

            switch (decision)
            {
                case FileRoutingDecision.DirectSend:
                    directSendCount++;
                    estimatedTokens += EstimateTokens(fileSize);
                    break;
                case FileRoutingDecision.RagChunks:
                    ragChunkCount++;
                    estimatedTokens += EstimateTokens(fileSize);
                    break;
                case FileRoutingDecision.Skipped:
                    skippedCount++;
                    skippedFiles.Add(new SkippedFileInfo
                    {
                        FilePath = relativePath,
                        FileSizeBytes = fileSize,
                        Reason = $"File exceeds size limit ({fileSize / 1024} KB)",
                        ReasonCode = reason
                    });
                    break;
            }
        }

        // Estimate cost (approximate)
        var estimatedCost = estimatedTokens * 0.00003m; // GPT-4 pricing approximation

        // Check thresholds
        var requiresApproval = estimatedTokens > _approvalOptions.ApprovalThresholdTokens 
            || estimatedCost > _approvalOptions.ApprovalThresholdCost;

        if (estimatedTokens > _approvalOptions.WarnThresholdTokens)
        {
            warnings.Add($"Large job: estimated {estimatedTokens:N0} tokens");
        }

        if (estimatedCost > _approvalOptions.WarnThresholdCost)
        {
            warnings.Add($"High cost estimate: ${estimatedCost:F2}");
        }

        if (skippedFiles.Count > 0)
        {
            warnings.Add($"{skippedFiles.Count} files will be skipped (too large or binary)");
        }

        // Estimate processing time (very rough: 1 second per 1000 tokens)
        var estimatedTimeSeconds = (int)(estimatedTokens / 1000) + (directSendCount + ragChunkCount);

        return new AnalysisEstimation
        {
            DirectSendCount = directSendCount,
            RagChunkCount = ragChunkCount,
            SkippedCount = skippedCount,
            EstimatedTokens = estimatedTokens,
            EstimatedCost = estimatedCost,
            EstimatedProcessingTimeSeconds = estimatedTimeSeconds,
            RequiresApproval = requiresApproval,
            Warnings = warnings,
            FileTypeBreakdown = fileTypeBreakdown,
            SkippedFiles = skippedFiles.Take(50).ToList() // Limit to first 50
        };
    }

    private bool IsExcludedPath(string relativePath)
    {
        foreach (var dir in ExcludedDirectories)
        {
            if (relativePath.Contains($"{Path.DirectorySeparatorChar}{dir}{Path.DirectorySeparatorChar}") ||
                relativePath.Contains($"{Path.AltDirectorySeparatorChar}{dir}{Path.AltDirectorySeparatorChar}") ||
                relativePath.StartsWith($"{dir}{Path.DirectorySeparatorChar}") ||
                relativePath.StartsWith($"{dir}{Path.AltDirectorySeparatorChar}"))
            {
                return true;
            }
        }
        return false;
    }

    private static bool IsConfigFile(string relativePath)
    {
        var fileName = Path.GetFileName(relativePath).ToLowerInvariant();
        return fileName.EndsWith(".json") || fileName.EndsWith(".yaml") || 
               fileName.EndsWith(".yml") || fileName.EndsWith(".xml") ||
               fileName.EndsWith(".config") || fileName.EndsWith(".toml") ||
               fileName == "dockerfile" || fileName == ".env" ||
               fileName == "makefile" || fileName == "cmakelists.txt";
    }

    private static int EstimateTokens(long fileSizeBytes)
    {
        // Rough estimate: ~4 characters per token for code
        return (int)(fileSizeBytes / 4);
    }

    private static string? DetectLanguage(string extension)
    {
        return extension.ToLowerInvariant() switch
        {
            ".cs" => "csharp",
            ".ts" or ".tsx" => "typescript",
            ".js" or ".jsx" => "javascript",
            ".py" => "python",
            ".java" => "java",
            ".go" => "go",
            ".rs" => "rust",
            ".cpp" or ".c" or ".h" or ".hpp" => "cpp",
            ".rb" => "ruby",
            ".php" => "php",
            ".swift" => "swift",
            ".kt" => "kotlin",
            ".scala" => "scala",
            ".vue" or ".svelte" => "javascript",
            ".razor" or ".cshtml" => "csharp",
            _ => null
        };
    }
}
