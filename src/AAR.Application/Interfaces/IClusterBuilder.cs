// =============================================================================
// AAR.Application - Interfaces/IClusterBuilder.cs
// Phase 2: Group files into semantic clusters using embeddings
// =============================================================================

using AAR.Domain.Entities;

namespace AAR.Application.Interfaces;

/// <summary>
/// Groups related files into semantic clusters for batch analysis.
/// Implements Phase 2 of the cluster-based analysis pipeline.
/// Uses embeddings and similarity metrics to group files.
/// Example: 200 files → 15 clusters (services, controllers, repositories, etc.)
/// </summary>
public interface IClusterBuilder
{
    /// <summary>
    /// Build clusters from a list of file summaries.
    /// Uses semantic similarity to group related files.
    /// </summary>
    Task<List<AnalysisCluster>> BuildClustersAsync(
        List<FileSummary> fileSummaries,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Detect files that should be analyzed individually (security-sensitive, high-complexity, etc.)
    /// These files are marked for Phase 4 (targeted deep dive).
    /// </summary>
    List<FileSummary> DetectHighPriorityFiles(
        List<FileSummary> fileSummaries,
        float complexityThreshold = 25.0f,
        int lineCountThreshold = 1000);

    /// <summary>
    /// Compute semantic similarity between two files (0.0-1.0).
    /// </summary>
    float ComputeSimilarity(FileSummary file1, FileSummary file2);
}
