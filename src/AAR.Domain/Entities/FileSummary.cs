// =============================================================================
// AAR.Domain - Entities/FileSummary.cs
// Static analysis metadata for a single file - used in clustering
// =============================================================================

namespace AAR.Domain.Entities;

/// <summary>
/// Static metadata for a source file (extracted without LLM).
/// Used for clustering and batch analysis instead of per-file LLM calls.
/// </summary>
public class FileSummary
{
    /// <summary>
    /// Unique identifier
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// Relative path from project root
    /// </summary>
    public required string RelativePath { get; set; }

    /// <summary>
    /// File language (cs, ts, java, py, etc.)
    /// </summary>
    public required string Language { get; set; }

    /// <summary>
    /// Total lines of code
    /// </summary>
    public int LinesOfCode { get; set; }

    /// <summary>
    /// Number of methods/functions in this file
    /// </summary>
    public int MethodCount { get; set; }

    /// <summary>
    /// Average cyclomatic complexity across methods
    /// </summary>
    public float AverageCyclomaticComplexity { get; set; }

    /// <summary>
    /// Maximum cyclomatic complexity in any single method
    /// </summary>
    public float MaxCyclomaticComplexity { get; set; }

    /// <summary>
    /// List of direct dependencies/imports (namespace.Class format)
    /// </summary>
    public List<string> Dependencies { get; set; } = new();

    /// <summary>
    /// List of external dependencies (NuGet/npm/etc packages)
    /// </summary>
    public List<string> ExternalDependencies { get; set; } = new();

    /// <summary>
    /// Semantic hash of file content for caching
    /// </summary>
    public string? ContentHash { get; set; }

    /// <summary>
    /// Risk level based on metrics (Low, Medium, High, Critical)
    /// </summary>
    public string RiskLevel { get; set; } = "Low";

    /// <summary>
    /// File name for reference
    /// </summary>
    public string FileName => Path.GetFileName(RelativePath);

    /// <summary>
    /// Directory name for reference
    /// </summary>
    public string DirectoryName => Path.GetDirectoryName(RelativePath) ?? "";

    /// <summary>
    /// Timestamp when metrics were extracted
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
