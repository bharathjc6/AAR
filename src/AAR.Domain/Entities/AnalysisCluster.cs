// =============================================================================
// AAR.Domain - Entities/AnalysisCluster.cs
// Semantic cluster of related files for batch LLM analysis
// =============================================================================

namespace AAR.Domain.Entities;

/// <summary>
/// A semantic cluster of related files.
/// Instead of analyzing files one-by-one with LLM, we group related files
/// into clusters and analyze each cluster as a unit.
/// E.g., 200 files → 15 clusters (service layer, controller layer, etc.)
/// </summary>
public class AnalysisCluster
{
    /// <summary>
    /// Unique identifier for this cluster
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// Human-readable cluster name (e.g., "Services", "Controllers", "Data Access")
    /// </summary>
    public required string Name { get; set; }

    /// <summary>
    /// Description of what this cluster represents
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// Files in this cluster
    /// </summary>
    public List<FileSummary> Files { get; set; } = new();

    /// <summary>
    /// Common theme/pattern detected in this cluster
    /// </summary>
    public string? Theme { get; set; }

    /// <summary>
    /// Primary language in this cluster
    /// </summary>
    public string PrimaryLanguage { get; set; } = "cs";

    /// <summary>
    /// Average cyclomatic complexity for all files in cluster
    /// </summary>
    public float AverageComplexity => Files.Count > 0 
        ? Files.Average(f => f.MaxCyclomaticComplexity) 
        : 0;

    /// <summary>
    /// Total lines of code in cluster
    /// </summary>
    public int TotalLinesOfCode => Files.Sum(f => f.LinesOfCode);

    /// <summary>
    /// Embedding vector for semantic similarity (will be populated by clustering algorithm)
    /// </summary>
    public float[]? EmbeddingVector { get; set; }

    /// <summary>
    /// Risk level of the cluster (Low, Medium, High, Critical)
    /// </summary>
    public string RiskLevel { get; set; } = "Low";

    /// <summary>
    /// Whether this cluster has been analyzed by LLM yet
    /// </summary>
    public bool IsAnalyzed { get; set; }

    /// <summary>
    /// Timestamp of cluster creation
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
