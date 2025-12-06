// =============================================================================
// AAR.Application - DTOs/ProjectDto.cs
// Data Transfer Objects for Project-related operations
// =============================================================================

using AAR.Domain.Enums;

namespace AAR.Application.DTOs;

/// <summary>
/// DTO for creating a new project from a zip file
/// </summary>
public record CreateProjectFromZipRequest
{
    /// <summary>
    /// Name for the project
    /// </summary>
    public required string Name { get; init; }
    
    /// <summary>
    /// Optional description
    /// </summary>
    public string? Description { get; init; }
}

/// <summary>
/// DTO for creating a new project from a Git repository
/// </summary>
public record CreateProjectFromGitRequest
{
    /// <summary>
    /// Name for the project
    /// </summary>
    public required string Name { get; init; }
    
    /// <summary>
    /// Git repository URL (HTTPS)
    /// </summary>
    public required string GitRepoUrl { get; init; }
    
    /// <summary>
    /// Optional description
    /// </summary>
    public string? Description { get; init; }
}

/// <summary>
/// Response DTO for project creation
/// </summary>
public record ProjectCreatedResponse
{
    /// <summary>
    /// The created project ID
    /// </summary>
    public Guid ProjectId { get; init; }
    
    /// <summary>
    /// Project name
    /// </summary>
    public string Name { get; init; } = string.Empty;
    
    /// <summary>
    /// Current status
    /// </summary>
    public ProjectStatus Status { get; init; }
    
    /// <summary>
    /// Creation timestamp
    /// </summary>
    public DateTime CreatedAt { get; init; }
}

/// <summary>
/// DTO for project summary in lists
/// </summary>
public record ProjectSummaryDto
{
    public Guid Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string? Description { get; init; }
    public ProjectStatus Status { get; init; }
    public string StatusText => Status.ToString();
    public DateTime CreatedAt { get; init; }
    public DateTime? AnalysisCompletedAt { get; init; }
    public int FileCount { get; init; }
    public int TotalLinesOfCode { get; init; }
    public int? HealthScore { get; init; }
}

/// <summary>
/// DTO for detailed project information
/// </summary>
public record ProjectDetailDto
{
    public Guid Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string? Description { get; init; }
    public string? GitRepoUrl { get; init; }
    public string? OriginalFileName { get; init; }
    public ProjectStatus Status { get; init; }
    public string StatusText => Status.ToString();
    public string? ErrorMessage { get; init; }
    public DateTime CreatedAt { get; init; }
    public DateTime? AnalysisStartedAt { get; init; }
    public DateTime? AnalysisCompletedAt { get; init; }
    public int FileCount { get; init; }
    public int TotalLinesOfCode { get; init; }
    public bool HasReport { get; init; }
    public ReportSummaryDto? ReportSummary { get; init; }
}

/// <summary>
/// DTO for report summary
/// </summary>
public record ReportSummaryDto
{
    public Guid ReportId { get; init; }
    public int HealthScore { get; init; }
    public int HighSeverityCount { get; init; }
    public int MediumSeverityCount { get; init; }
    public int LowSeverityCount { get; init; }
    public int TotalFindingsCount { get; init; }
    public bool HasPdfReport { get; init; }
}

/// <summary>
/// Response DTO for starting analysis
/// </summary>
public record StartAnalysisResponse
{
    public Guid ProjectId { get; init; }
    public ProjectStatus Status { get; init; }
    public string Message { get; init; } = string.Empty;
}
