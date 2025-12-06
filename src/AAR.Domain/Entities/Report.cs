// =============================================================================
// AAR.Domain - Entities/Report.cs
// Represents the consolidated analysis report for a project
// =============================================================================

namespace AAR.Domain.Entities;

/// <summary>
/// Represents the consolidated analysis report for a project
/// </summary>
public class Report : BaseEntity
{
    /// <summary>
    /// Project this report belongs to
    /// </summary>
    public Guid ProjectId { get; private set; }
    
    /// <summary>
    /// Overall summary of the analysis
    /// </summary>
    public string Summary { get; private set; } = string.Empty;
    
    /// <summary>
    /// High-level recommendations
    /// </summary>
    public IList<string> Recommendations { get; private set; } = [];
    
    /// <summary>
    /// Overall health score (0-100)
    /// </summary>
    public int HealthScore { get; private set; }
    
    /// <summary>
    /// Count of high severity findings
    /// </summary>
    public int HighSeverityCount { get; private set; }
    
    /// <summary>
    /// Count of medium severity findings
    /// </summary>
    public int MediumSeverityCount { get; private set; }
    
    /// <summary>
    /// Count of low severity findings
    /// </summary>
    public int LowSeverityCount { get; private set; }
    
    /// <summary>
    /// Total number of findings
    /// </summary>
    public int TotalFindingsCount { get; private set; }
    
    /// <summary>
    /// Blob path to the PDF report (if generated)
    /// </summary>
    public string? PdfReportPath { get; private set; }
    
    /// <summary>
    /// Blob path to the JSON report
    /// </summary>
    public string? JsonReportPath { get; private set; }
    
    /// <summary>
    /// Blob path to generated patch files (if any)
    /// </summary>
    public string? PatchFilesPath { get; private set; }
    
    /// <summary>
    /// Report version for tracking schema changes
    /// </summary>
    public string ReportVersion { get; private set; } = "1.0";
    
    /// <summary>
    /// Time taken to complete the analysis (in seconds)
    /// </summary>
    public int AnalysisDurationSeconds { get; private set; }

    /// <summary>
    /// Navigation property to the project
    /// </summary>
    public Project? Project { get; private set; }
    
    /// <summary>
    /// Findings in this report
    /// </summary>
    public ICollection<ReviewFinding> Findings { get; private set; } = [];

    // Private constructor for EF Core
    private Report() { }

    /// <summary>
    /// Creates a new report for a project
    /// </summary>
    public static Report Create(Guid projectId)
    {
        return new Report
        {
            ProjectId = projectId
        };
    }

    /// <summary>
    /// Updates the report with aggregated statistics
    /// </summary>
    public void UpdateStatistics(
        string summary,
        IList<string> recommendations,
        int healthScore,
        int highCount,
        int mediumCount,
        int lowCount,
        int durationSeconds)
    {
        Summary = summary;
        Recommendations = recommendations;
        HealthScore = Math.Clamp(healthScore, 0, 100);
        HighSeverityCount = highCount;
        MediumSeverityCount = mediumCount;
        LowSeverityCount = lowCount;
        TotalFindingsCount = highCount + mediumCount + lowCount;
        AnalysisDurationSeconds = durationSeconds;
        SetUpdated();
    }

    /// <summary>
    /// Sets the path to the JSON report
    /// </summary>
    public void SetJsonReportPath(string path)
    {
        JsonReportPath = path;
        SetUpdated();
    }

    /// <summary>
    /// Sets the path to the PDF report
    /// </summary>
    public void SetPdfReportPath(string path)
    {
        PdfReportPath = path;
        SetUpdated();
    }

    /// <summary>
    /// Sets the path to patch files
    /// </summary>
    public void SetPatchFilesPath(string path)
    {
        PatchFilesPath = path;
        SetUpdated();
    }
}
