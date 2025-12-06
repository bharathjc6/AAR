// =============================================================================
// AAR.Application - DTOs/ReportDto.cs
// Data Transfer Objects for Report-related operations
// =============================================================================

using AAR.Domain.Enums;

namespace AAR.Application.DTOs;

/// <summary>
/// Full report DTO with all findings
/// </summary>
public record ReportDto
{
    public Guid Id { get; init; }
    public Guid ProjectId { get; init; }
    public string ProjectName { get; init; } = string.Empty;
    public string Summary { get; init; } = string.Empty;
    public IList<string> Recommendations { get; init; } = [];
    public int HealthScore { get; init; }
    public StatisticsDto Statistics { get; init; } = new();
    public IList<FindingDto> Findings { get; init; } = [];
    public string ReportVersion { get; init; } = string.Empty;
    public int AnalysisDurationSeconds { get; init; }
    public DateTime GeneratedAt { get; init; }
    public string? PdfDownloadUrl { get; init; }
    public string? JsonDownloadUrl { get; init; }
}

/// <summary>
/// Statistics section of the report
/// </summary>
public record StatisticsDto
{
    public int TotalFiles { get; init; }
    public int AnalyzedFiles { get; init; }
    public int TotalLinesOfCode { get; init; }
    public int HighSeverityCount { get; init; }
    public int MediumSeverityCount { get; init; }
    public int LowSeverityCount { get; init; }
    public int TotalFindingsCount { get; init; }
    public IDictionary<string, int> FindingsByCategory { get; init; } = new Dictionary<string, int>();
}

/// <summary>
/// Individual finding DTO
/// </summary>
public record FindingDto
{
    public Guid Id { get; init; }
    public string? FilePath { get; init; }
    public LineRangeDto? LineRange { get; init; }
    public FindingCategory Category { get; init; }
    public string CategoryText => Category.ToString();
    public Severity Severity { get; init; }
    public string SeverityText => Severity.ToString();
    public AgentType AgentType { get; init; }
    public string AgentTypeText => AgentType.ToString();
    public string Description { get; init; } = string.Empty;
    public string Explanation { get; init; } = string.Empty;
    public string? SuggestedFix { get; init; }
    public string? FixedCodeSnippet { get; init; }
    public string? OriginalCodeSnippet { get; init; }
}

/// <summary>
/// Line range DTO
/// </summary>
public record LineRangeDto
{
    public int Start { get; init; }
    public int End { get; init; }
}

/// <summary>
/// Agent analysis response (from OpenAI)
/// </summary>
public record AgentAnalysisResponse
{
    public List<AgentFinding> Findings { get; init; } = [];
    public string Summary { get; init; } = string.Empty;
    public List<string> Recommendations { get; init; } = [];
}

/// <summary>
/// Individual finding from agent analysis
/// </summary>
public record AgentFinding
{
    public string Id { get; init; } = string.Empty;
    public string? FilePath { get; init; }
    public AgentLineRange? LineRange { get; init; }
    public string Category { get; init; } = string.Empty;
    public string Severity { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public string Explanation { get; init; } = string.Empty;
    public string? SuggestedFix { get; init; }
    public string? FixedCodeSnippet { get; init; }
    public string? OriginalCodeSnippet { get; init; }
}

/// <summary>
/// Line range in agent response
/// </summary>
public record AgentLineRange
{
    public int Start { get; init; }
    public int End { get; init; }
}
