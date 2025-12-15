// =============================================================================
// AAR.Infrastructure - Services/Analysis/ClusterBuilder.cs
// Phase 2: Group files into semantic clusters
// =============================================================================

using AAR.Application.Configuration;
using AAR.Application.Interfaces;
using AAR.Domain.Entities;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AAR.Infrastructure.Services.Analysis;

/// <summary>
/// Groups related files into semantic clusters using similarity metrics.
/// Reduces 200+ files to 10-20 clusters for batch LLM analysis.
/// </summary>
public class ClusterBuilder : IClusterBuilder
{
    private readonly ClusterAnalysisOptions _options;
    private readonly ILogger<ClusterBuilder> _logger;

    public ClusterBuilder(
        IOptions<ClusterAnalysisOptions> options,
        ILogger<ClusterBuilder> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public async Task<List<AnalysisCluster>> BuildClustersAsync(
        List<FileSummary> fileSummaries,
        CancellationToken cancellationToken = default)
    {
        if (fileSummaries.Count == 0)
            return new();

        _logger.LogInformation("ClusterBuilder: Grouping {Count} files into clusters", fileSummaries.Count);

        // Start with directory-based clustering (fast heuristic)
        var directoryClusters = ClusterByDirectory(fileSummaries);

        _logger.LogInformation("ClusterBuilder: Created {Count} initial clusters by directory", directoryClusters.Count);

        // Refine clusters by similarity
        var refinedClusters = RefineClustersByLanguageAndTheme(directoryClusters);

        // Ensure we stay within min/max cluster limits
        var finalClusters = NormalizeClusterCount(refinedClusters, fileSummaries.Count);

        _logger.LogInformation("ClusterBuilder: Final cluster count: {Count}", finalClusters.Count);

        return await Task.FromResult(finalClusters);
    }

    public List<FileSummary> DetectHighPriorityFiles(
        List<FileSummary> fileSummaries,
        float complexityThreshold = 25.0f,
        int lineCountThreshold = 1000)
    {
        return fileSummaries
            .Where(f => f.MaxCyclomaticComplexity > complexityThreshold ||
                       f.LinesOfCode > lineCountThreshold ||
                       f.RiskLevel is "High" or "Critical")
            .OrderByDescending(f => f.MaxCyclomaticComplexity)
            .ToList();
    }

    public float ComputeSimilarity(FileSummary file1, FileSummary file2)
    {
        float score = 0f;

        // Language similarity (exact match = 1.0)
        if (file1.Language == file2.Language)
            score += 0.3f;

        // Directory proximity (same dir = 1.0, parent = 0.7, etc.)
        var dir1 = Path.GetDirectoryName(file1.RelativePath) ?? "";
        var dir2 = Path.GetDirectoryName(file2.RelativePath) ?? "";
        var dirSimilarity = ComputeDirectorySimilarity(dir1, dir2);
        score += dirSimilarity * 0.3f;

        // Dependency overlap
        var commonDeps = file1.Dependencies.Intersect(file2.Dependencies).Count();
        var totalDeps = file1.Dependencies.Union(file2.Dependencies).Count();
        var depSimilarity = totalDeps > 0 ? (float)commonDeps / totalDeps : 0f;
        score += depSimilarity * 0.2f;

        // Complexity similarity (files with similar complexity often belong together)
        var complexityDiff = Math.Abs(file1.MaxCyclomaticComplexity - file2.MaxCyclomaticComplexity);
        var complexitySimilarity = Math.Max(0, 1f - (complexityDiff / 50f)); // Normalize to 0-1
        score += complexitySimilarity * 0.2f;

        return Math.Clamp(score, 0f, 1f);
    }

    private List<AnalysisCluster> ClusterByDirectory(List<FileSummary> files)
    {
        var clusters = new Dictionary<string, AnalysisCluster>();

        foreach (var file in files)
        {
            var dirName = Path.GetDirectoryName(file.RelativePath) ?? "root";
            var topLevelDir = dirName.Split(Path.DirectorySeparatorChar)[0];

            if (!clusters.ContainsKey(topLevelDir))
            {
                clusters[topLevelDir] = new AnalysisCluster
                {
                    Id = Guid.NewGuid(),
                    Name = FormatClusterName(topLevelDir),
                    Description = $"Files in {topLevelDir} directory",
                    Theme = DetectTheme(topLevelDir),
                    PrimaryLanguage = "cs" // Default, will be refined
                };
            }

            clusters[topLevelDir].Files.Add(file);
        }

        return clusters.Values.ToList();
    }

    private List<AnalysisCluster> RefineClustersByLanguageAndTheme(List<AnalysisCluster> clusters)
    {
        var refined = new List<AnalysisCluster>();

        foreach (var cluster in clusters)
        {
            if (cluster.Files.Count == 0) continue;

            // Group by language within cluster
            var byLanguage = cluster.Files
                .GroupBy(f => f.Language)
                .ToList();

            if (byLanguage.Count > 1)
            {
                // Split cluster by language if significantly different
                foreach (var langGroup in byLanguage)
                {
                    var subCluster = new AnalysisCluster
                    {
                        Id = Guid.NewGuid(),
                        Name = $"{cluster.Name} ({langGroup.Key.ToUpper()})",
                        Description = $"{cluster.Description} - {langGroup.Key}",
                        Theme = cluster.Theme,
                        PrimaryLanguage = langGroup.Key,
                        Files = langGroup.ToList(),
                        RiskLevel = CalculateClusterRiskLevel(langGroup.ToList())
                    };

                    refined.Add(subCluster);
                }
            }
            else
            {
                cluster.PrimaryLanguage = byLanguage[0].Key;
                cluster.RiskLevel = CalculateClusterRiskLevel(cluster.Files);
                refined.Add(cluster);
            }
        }

        return refined;
    }

    private List<AnalysisCluster> NormalizeClusterCount(List<AnalysisCluster> clusters, int totalFiles)
    {
        // Merge small clusters if we have too many
        if (clusters.Count > _options.MaxClusters)
        {
            _logger.LogWarning("Cluster count {Count} exceeds max {Max}. Merging smallest clusters.",
                clusters.Count, _options.MaxClusters);

            while (clusters.Count > _options.MaxClusters)
            {
                var smallest = clusters.OrderBy(c => c.Files.Count).FirstOrDefault();
                if (smallest == null) break;

                var mostSimilar = clusters
                    .Where(c => c.Id != smallest.Id)
                    .OrderByDescending(c => ComputeSimilarity(smallest.Files[0], c.Files[0]))
                    .FirstOrDefault();

                if (mostSimilar != null)
                {
                    mostSimilar.Files.AddRange(smallest.Files);
                    clusters.Remove(smallest);
                }
            }
        }

        // Split large clusters if we have too few
        if (clusters.Count < _options.MinClusters && clusters.Count > 0)
        {
            var largestCluster = clusters.OrderByDescending(c => c.Files.Count).First();

            // Don't split if resulting clusters would be too small
            if (largestCluster.Files.Count > 20)
            {
                var subClusters = SplitClusterBySimilarity(largestCluster);
                clusters.Remove(largestCluster);
                clusters.AddRange(subClusters);
            }
        }

        return clusters;
    }

    private List<AnalysisCluster> SplitClusterBySimilarity(AnalysisCluster cluster)
    {
        _logger.LogInformation("Splitting large cluster '{Name}' with {Count} files",
            cluster.Name, cluster.Files.Count);

        var subClusters = new List<AnalysisCluster>();
        var unassigned = new List<FileSummary>(cluster.Files);

        while (unassigned.Count > 0)
        {
            var seed = unassigned[0];
            var subCluster = new AnalysisCluster
            {
                Id = Guid.NewGuid(),
                Name = $"{cluster.Name} (Part {subClusters.Count + 1})",
                Theme = cluster.Theme,
                PrimaryLanguage = cluster.PrimaryLanguage
            };

            subCluster.Files.Add(seed);
            unassigned.Remove(seed);

            // Find similar files to group with seed
            var similarFiles = unassigned
                .Where(f => ComputeSimilarity(seed, f) > _options.SimilarityThreshold)
                .ToList();

            foreach (var file in similarFiles)
            {
                subCluster.Files.Add(file);
                unassigned.Remove(file);
            }

            subClusters.Add(subCluster);
        }

        return subClusters;
    }

    private float ComputeDirectorySimilarity(string dir1, string dir2)
    {
        if (dir1 == dir2) return 1.0f;
        if (dir1.StartsWith(dir2) || dir2.StartsWith(dir1)) return 0.7f;

        var parts1 = dir1.Split(Path.DirectorySeparatorChar);
        var parts2 = dir2.Split(Path.DirectorySeparatorChar);

        var commonParts = parts1.Intersect(parts2).Count();
        return commonParts > 0 ? (float)commonParts / Math.Max(parts1.Length, parts2.Length) : 0f;
    }

    private string DetectTheme(string directoryName)
    {
        var lower = directoryName.ToLowerInvariant();

        return lower switch
        {
            var x when x.Contains("service") => "Services",
            var x when x.Contains("controller") => "Controllers",
            var x when x.Contains("repository") || x.Contains("data") => "Data Access",
            var x when x.Contains("model") || x.Contains("entity") => "Models",
            var x when x.Contains("interface") || x.Contains("contract") => "Interfaces",
            var x when x.Contains("middleware") => "Middleware",
            var x when x.Contains("test") || x.Contains("spec") => "Tests",
            var x when x.Contains("util") || x.Contains("helper") => "Utilities",
            _ => "Mixed"
        };
    }

    private string FormatClusterName(string directoryName)
    {
        var theme = DetectTheme(directoryName);
        return theme != "Mixed" ? theme : ToPascalCase(directoryName);
    }

    private string ToPascalCase(string text)
    {
        return string.Concat(text
            .Split(new[] { '_', '-', ' ' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(word => char.ToUpperInvariant(word[0]) + word.Substring(1).ToLowerInvariant()));
    }

    private string CalculateClusterRiskLevel(List<FileSummary> files)
    {
        if (files.Count == 0) return "Low";

        var criticalCount = files.Count(f => f.RiskLevel == "Critical");
        var highCount = files.Count(f => f.RiskLevel == "High");
        var avgComplexity = files.Average(f => f.MaxCyclomaticComplexity);

        if (criticalCount > 0) return "Critical";
        if (highCount > 0 || avgComplexity > 25) return "High";
        if (avgComplexity > 15) return "Medium";

        return "Low";
    }
}
