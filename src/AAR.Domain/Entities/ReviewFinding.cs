// =============================================================================
// AAR.Domain - Entities/ReviewFinding.cs
// Represents a single finding from the code analysis
// =============================================================================

using AAR.Domain.Enums;
using AAR.Domain.ValueObjects;

namespace AAR.Domain.Entities;

/// <summary>
/// Represents a single finding from an analysis agent
/// </summary>
public class ReviewFinding : BaseEntity
{
    /// <summary>
    /// Project this finding belongs to
    /// </summary>
    public Guid ProjectId { get; private set; }
    
    /// <summary>
    /// File this finding relates to (null for project-level findings)
    /// </summary>
    public Guid? FileRecordId { get; private set; }
    
    /// <summary>
    /// Report this finding belongs to
    /// </summary>
    public Guid ReportId { get; private set; }
    
    /// <summary>
    /// Relative file path (denormalized for convenience)
    /// </summary>
    public string? FilePath { get; private set; }
    
    /// <summary>
    /// Line range where the issue was found
    /// </summary>
    public LineRange? LineRange { get; private set; }
    
    /// <summary>
    /// Category of the finding
    /// </summary>
    public FindingCategory Category { get; private set; }
    
    /// <summary>
    /// Severity of the finding
    /// </summary>
    public Severity Severity { get; private set; }
    
    /// <summary>
    /// Which agent produced this finding
    /// </summary>
    public AgentType AgentType { get; private set; }
    
    /// <summary>
    /// Short description of the issue
    /// </summary>
    public string Description { get; private set; } = string.Empty;
    
    /// <summary>
    /// Detailed explanation of why this is an issue
    /// </summary>
    public string Explanation { get; private set; } = string.Empty;
    
    /// <summary>
    /// Suggested fix for the issue
    /// </summary>
    public string? SuggestedFix { get; private set; }
    
    /// <summary>
    /// Optional fixed code snippet
    /// </summary>
    public string? FixedCodeSnippet { get; private set; }
    
    /// <summary>
    /// Original code snippet (for context)
    /// </summary>
    public string? OriginalCodeSnippet { get; private set; }

    /// <summary>
    /// Symbol or identifier the finding refers to (if applicable)
    /// </summary>
    public string? Symbol { get; private set; }

    /// <summary>
    /// Confidence score (0.0 - 1.0) for this finding
    /// </summary>
    public double Confidence { get; private set; }

    /// <summary>
    /// Navigation property to file record
    /// </summary>
    public FileRecord? FileRecord { get; private set; }
    
    /// <summary>
    /// Navigation property to report
    /// </summary>
    public Report? Report { get; private set; }

    // Private constructor for EF Core
    private ReviewFinding() { }

    /// <summary>
    /// Creates a new review finding
    /// </summary>
    public static ReviewFinding Create(
        Guid projectId,
        Guid reportId,
        AgentType agentType,
        FindingCategory category,
        Severity severity,
        string description,
        string explanation,
        string? filePath = null,
        Guid? fileRecordId = null,
        LineRange? lineRange = null,
        string? suggestedFix = null,
        string? fixedCodeSnippet = null,
        string? originalCodeSnippet = null,
        string? symbol = null,
        double confidence = 0.0)
    {
        return new ReviewFinding
        {
            ProjectId = projectId,
            ReportId = reportId,
            FileRecordId = fileRecordId,
            FilePath = filePath,
            LineRange = lineRange,
            AgentType = agentType,
            Category = category,
            Severity = severity,
            Description = description,
            Explanation = explanation,
            SuggestedFix = suggestedFix,
            FixedCodeSnippet = fixedCodeSnippet,
            OriginalCodeSnippet = originalCodeSnippet,
            Symbol = symbol,
            Confidence = confidence
        };
    }
}
