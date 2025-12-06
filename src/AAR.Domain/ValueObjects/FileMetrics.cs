// =============================================================================
// AAR.Domain - ValueObjects/FileMetrics.cs
// Computed metrics for a source file
// =============================================================================

namespace AAR.Domain.ValueObjects;

/// <summary>
/// Represents computed metrics for a source file
/// </summary>
public record FileMetrics
{
    /// <summary>
    /// Lines of code (excluding blank lines and comments)
    /// </summary>
    public int LinesOfCode { get; init; }
    
    /// <summary>
    /// Total lines in the file
    /// </summary>
    public int TotalLines { get; init; }
    
    /// <summary>
    /// Approximate cyclomatic complexity (heuristic-based)
    /// </summary>
    public int CyclomaticComplexity { get; init; }
    
    /// <summary>
    /// Number of classes/types defined
    /// </summary>
    public int TypeCount { get; init; }
    
    /// <summary>
    /// Number of methods/functions defined
    /// </summary>
    public int MethodCount { get; init; }
    
    /// <summary>
    /// Number of namespaces used
    /// </summary>
    public int NamespaceCount { get; init; }

    /// <summary>
    /// Creates default empty metrics
    /// </summary>
    public static FileMetrics Empty => new()
    {
        LinesOfCode = 0,
        TotalLines = 0,
        CyclomaticComplexity = 0,
        TypeCount = 0,
        MethodCount = 0,
        NamespaceCount = 0
    };
}
