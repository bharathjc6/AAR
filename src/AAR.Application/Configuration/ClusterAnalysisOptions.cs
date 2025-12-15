// =============================================================================
// AAR.Application - Configuration/ClusterAnalysisOptions.cs
// Configuration for cluster-based analysis pipeline
// =============================================================================

namespace AAR.Application.Configuration;

/// <summary>
/// Configuration for clustering and analysis thresholds.
/// Makes the analysis pipeline tunable without code changes.
/// </summary>
public class ClusterAnalysisOptions
{
    /// <summary>
    /// Configuration section name in appsettings.json
    /// </summary>
    public const string SectionName = "ClusterAnalysis";

    /// <summary>
    /// Minimum number of clusters to target (algorithm will split if needed)
    /// </summary>
    public int MinClusters { get; set; } = 5;

    /// <summary>
    /// Maximum number of clusters to create (prevents over-fragmentation)
    /// </summary>
    public int MaxClusters { get; set; } = 25;

    /// <summary>
    /// Similarity threshold for grouping files into clusters (0.0-1.0)
    /// Higher = stricter grouping, more clusters
    /// </summary>
    public float SimilarityThreshold { get; set; } = 0.65f;

    /// <summary>
    /// Complexity threshold for forcing per-file deep dive analysis (0-100)
    /// </summary>
    public float DeepDiveComplexityThreshold { get; set; } = 25.0f;

    /// <summary>
    /// Lines of code threshold for per-file analysis (0+)
    /// </summary>
    public int DeepDiveLineCountThreshold { get; set; } = 1000;

    /// <summary>
    /// Maximum number of parallel LLM calls when analyzing clusters
    /// </summary>
    public int MaxParallelLLMCalls { get; set; } = 3;

    /// <summary>
    /// Enable per-file caching based on file hash
    /// </summary>
    public bool EnableFileCaching { get; set; } = true;

    /// <summary>
    /// Whether to always run per-file analysis in addition to cluster analysis
    /// </summary>
    public bool AlwaysDeepDiveAllFiles { get; set; } = false;

    /// <summary>
    /// Maximum files to include in a single LLM cluster analysis prompt
    /// </summary>
    public int FilesPerClusterSummary { get; set; } = 20;

    /// <summary>
    /// Enable cluster-based analysis (Phase 2-3 of pipeline)
    /// If false, falls back to legacy per-file analysis
    /// </summary>
    public bool EnableClusterAnalysis { get; set; } = true;

    /// <summary>
    /// Enable Phase 1 static analysis extraction
    /// </summary>
    public bool EnableStaticAnalysis { get; set; } = true;
}
