// =============================================================================
// AAR.Infrastructure - Services/Analysis/StaticAnalyzer.cs
// Phase 1: Fast static code analysis without LLM calls
// =============================================================================

using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using AAR.Application.Interfaces;
using AAR.Domain.Entities;
using Microsoft.Extensions.Logging;

namespace AAR.Infrastructure.Services.Analysis;

/// <summary>
/// Extracts lightweight metrics from source files without calling LLM.
/// Focuses on: LOC, methods, complexity, dependencies.
/// </summary>
public class StaticAnalyzer : IStaticAnalyzer
{
    private readonly ICodeMetricsService _metricsService;
    private readonly ILogger<StaticAnalyzer> _logger;

    public StaticAnalyzer(
        ICodeMetricsService metricsService,
        ILogger<StaticAnalyzer> logger)
    {
        _metricsService = metricsService;
        _logger = logger;
    }

    public async Task<FileSummary> AnalyzeFileAsync(
        string filePath,
        string relativePath,
        CancellationToken cancellationToken = default)
    {
        var language = Path.GetExtension(filePath).TrimStart('.').ToLowerInvariant();
        var content = await File.ReadAllTextAsync(filePath, cancellationToken);
        var metrics = await _metricsService.CalculateMetricsAsync(filePath, cancellationToken);

        var summary = new FileSummary
        {
            Id = Guid.NewGuid(),
            RelativePath = relativePath,
            Language = language,
            LinesOfCode = metrics.LinesOfCode,
            MethodCount = metrics.MethodCount,
            AverageCyclomaticComplexity = metrics.CyclomaticComplexity > 0 ? metrics.CyclomaticComplexity / Math.Max(1, metrics.MethodCount) : 0,
            MaxCyclomaticComplexity = metrics.CyclomaticComplexity,
            Dependencies = ExtractDependencies(content, language),
            ExternalDependencies = ExtractExternalDependencies(content, language),
            ContentHash = ComputeFileHash(filePath),
            RiskLevel = CalculateRiskLevel(metrics),
        };

        return summary;
    }

    public async Task<List<FileSummary>> AnalyzeProjectAsync(
        string workingDirectory,
        CancellationToken cancellationToken = default)
    {
        var summaries = new List<FileSummary>();
        var sourceFiles = GetSourceFiles(workingDirectory).ToList();

        _logger.LogInformation("StaticAnalyzer: Analyzing {Count} files in {Directory}",
            sourceFiles.Count, workingDirectory);

        foreach (var file in sourceFiles)
        {
            if (cancellationToken.IsCancellationRequested) break;

            try
            {
                var relativePath = Path.GetRelativePath(workingDirectory, file);
                var summary = await AnalyzeFileAsync(file, relativePath, cancellationToken);
                summaries.Add(summary);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to analyze file {File}", file);
            }
        }

        _logger.LogInformation("StaticAnalyzer: Completed analysis of {Count} files", summaries.Count);
        return summaries;
    }

    public string ComputeFileHash(string filePath)
    {
        try
        {
            var fileInfo = new FileInfo(filePath);
            // Use size + last modified time for quick hash (alternative: use SHA256 on content)
            var data = $"{fileInfo.Length}:{fileInfo.LastWriteTimeUtc:O}";
            using (var hasher = SHA256.Create())
            {
                var hash = hasher.ComputeHash(Encoding.UTF8.GetBytes(data));
                return Convert.ToHexString(hash);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to compute hash for {File}", filePath);
            return string.Empty;
        }
    }

    /// <summary>
    /// Extract internal dependencies (imports, using statements, etc.)
    /// </summary>
    private List<string> ExtractDependencies(string content, string language)
    {
        var dependencies = new List<string>();

        if (language == "cs")
        {
            // Extract 'using' statements
            var usingPattern = new Regex(@"^\s*using\s+([^;]+);", RegexOptions.Multiline);
            foreach (Match match in usingPattern.Matches(content))
            {
                dependencies.Add(match.Groups[1].Value.Trim());
            }
        }
        else if (language is "ts" or "tsx" or "js" or "jsx")
        {
            // Extract import statements
            var importPattern = new Regex(@"^import\s+.*from\s+['""]([^'""]+ )['""];?", RegexOptions.Multiline);
            foreach (Match match in importPattern.Matches(content))
            {
                dependencies.Add(match.Groups[1].Value.Trim());
            }
        }
        else if (language == "py")
        {
            // Extract import statements
            var importPattern = new Regex(@"^(?:import|from)\s+([^\s]+)", RegexOptions.Multiline);
            foreach (Match match in importPattern.Matches(content))
            {
                dependencies.Add(match.Groups[1].Value.Trim());
            }
        }

        return dependencies.Distinct().ToList();
    }

    /// <summary>
    /// Extract external package dependencies (NuGet, npm, pip, etc.)
    /// </summary>
    private List<string> ExtractExternalDependencies(string content, string language)
    {
        var external = new List<string>();

        // For this MVP, we'll do simple pattern matching.
        // In production, you'd parse package.json, requirements.txt, .csproj, etc.
        if (language is "ts" or "tsx" or "js" or "jsx")
        {
            // Extract import from node_modules or common packages
            var pattern = new Regex(@"from\s+['""](@?[a-zA-Z0-9_\-]+)['""]");
            foreach (Match match in pattern.Matches(content))
            {
                var pkg = match.Groups[1].Value.Trim();
                if (!pkg.StartsWith("."))
                {
                    external.Add(pkg);
                }
            }
        }
        else if (language == "py")
        {
            // Extract module names that are commonly external
            var pattern = new Regex(@"^(?:import|from)\s+([a-zA-Z_][a-zA-Z0-9_]*)");
            foreach (Match match in pattern.Matches(content))
            {
                external.Add(match.Groups[1].Value.Trim());
            }
        }

        return external.Distinct().ToList();
    }

    /// <summary>
    /// Calculate risk level based on metrics
    /// </summary>
    private string CalculateRiskLevel(dynamic metrics)
    {
        float complexity = metrics.CyclomaticComplexity;
        int loc = metrics.LinesOfCode;
        int methods = metrics.MethodCount;

        int riskScore = 0;

        if (complexity > 30) riskScore += 3;
        else if (complexity > 20) riskScore += 2;
        else if (complexity > 10) riskScore += 1;

        if (loc > 1000) riskScore += 3;
        else if (loc > 500) riskScore += 2;
        else if (loc > 250) riskScore += 1;

        if (methods > 30) riskScore += 2;
        else if (methods > 20) riskScore += 1;

        return riskScore switch
        {
            >= 7 => "Critical",
            >= 5 => "High",
            >= 3 => "Medium",
            _ => "Low"
        };
    }

    private IEnumerable<string> GetSourceFiles(string workingDirectory)
    {
        var extensions = new[] { ".cs", ".ts", ".tsx", ".js", ".jsx", ".py", ".java", ".go", ".rs", ".cpp", ".c", ".h" };
        var excludedDirs = new[] { "node_modules", "bin", "obj", ".git", ".vs", ".idea", "packages", "dist", "build" };

        return Directory.EnumerateFiles(workingDirectory, "*.*", SearchOption.AllDirectories)
            .Where(f => extensions.Any(ext => f.EndsWith(ext, StringComparison.OrdinalIgnoreCase)))
            .Where(f => !excludedDirs.Any(dir => f.Contains($"{Path.DirectorySeparatorChar}{dir}{Path.DirectorySeparatorChar}")));
    }
}
