// =============================================================================
// AAR.Application - Services/ReportAggregator.cs
// Service for aggregating analysis results into a consolidated report
// =============================================================================

using AAR.Application.DTOs;
using AAR.Domain.Entities;
using AAR.Domain.Enums;
using AAR.Domain.Interfaces;
using AAR.Domain.ValueObjects;
using Microsoft.Extensions.Logging;

namespace AAR.Application.Services;

/// <summary>
/// Aggregates results from multiple analysis agents into a single report
/// </summary>
public class ReportAggregator : IReportAggregator
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<ReportAggregator> _logger;

    public ReportAggregator(
        IUnitOfWork unitOfWork,
        ILogger<ReportAggregator> logger)
    {
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task<Report> AggregateAsync(
        Guid projectId,
        IDictionary<AgentType, AgentAnalysisResponse> agentResponses,
        int analysisDurationSeconds,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Aggregating report for project: {ProjectId}", projectId);

        // Create the report
        var report = Report.Create(projectId);
        await _unitOfWork.Reports.AddAsync(report, cancellationToken);

        // Collect all findings
        var findings = new List<ReviewFinding>();
        var allRecommendations = new List<string>();
        var skippedFindings = new List<string>();

        foreach (var (agentType, response) in agentResponses)
        {
            _logger.LogDebug("Processing {AgentType} response with {FindingCount} findings",
                agentType, response.Findings.Count);

            allRecommendations.AddRange(response.Recommendations);

            foreach (var finding in response.Findings)
            {
                // Enforce evidence-first contract: require a file path and either a line range or a symbol
                var hasFile = !string.IsNullOrWhiteSpace(finding.FilePath);
                var hasSymbol = !string.IsNullOrWhiteSpace(finding.Symbol);
                var hasLineRange = finding.LineRange is not null && finding.LineRange.Start > 0;

                if (!hasFile || (!hasSymbol && !hasLineRange))
                {
                    _logger.LogWarning("Skipping finding from {AgentType} due to missing evidence: {Description}",
                        agentType, finding.Description);
                    skippedFindings.Add($"{agentType}: {finding.Description}");
                    continue;
                }

                var reviewFinding = MapToReviewFinding(projectId, report.Id, agentType, finding);
                findings.Add(reviewFinding);
            }
        }

        // Add findings to database
        if (findings.Count > 0)
        {
            await _unitOfWork.ReviewFindings.AddRangeAsync(findings, cancellationToken);
        }

        // Calculate statistics
        var highCount = findings.Count(f => f.Severity == Severity.High);
        var mediumCount = findings.Count(f => f.Severity == Severity.Medium);
        var lowCount = findings.Count(f => f.Severity == Severity.Low);

        // Calculate health score (simple algorithm)
        var healthScore = CalculateHealthScore(highCount, mediumCount, lowCount);

        // Generate summary (include skipped findings info)
        var summary = GenerateSummary(agentResponses, findings, healthScore, skippedFindings);

        // Deduplicate and limit recommendations
        var uniqueRecommendations = allRecommendations
            .Distinct()
            .Take(10)
            .ToList();

        // Update report with statistics
        report.UpdateStatistics(
            summary,
            uniqueRecommendations,
            healthScore,
            highCount,
            mediumCount,
            lowCount,
            analysisDurationSeconds);

        // No need to call UpdateAsync - the report entity is already tracked by the context
        // since we called AddAsync above. The changes will be saved automatically.
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Report aggregated for project {ProjectId}: {TotalFindings} findings, Health Score: {HealthScore}",
            projectId, findings.Count, healthScore);

        return report;
    }

    /// <inheritdoc/>
    public int CalculateHealthScore(int highCount, int mediumCount, int lowCount)
    {
        // Simple scoring algorithm:
        // Start at 100, deduct points for findings
        // High: -10 points each (max -50)
        // Medium: -3 points each (max -30)
        // Low: -1 point each (max -20)

        var score = 100;

        score -= Math.Min(highCount * 10, 50);
        score -= Math.Min(mediumCount * 3, 30);
        score -= Math.Min(lowCount * 1, 20);

        return Math.Max(0, score);
    }

    private static ReviewFinding MapToReviewFinding(
        Guid projectId,
        Guid reportId,
        AgentType agentType,
        AgentFinding finding)
    {
        // Parse category
        var category = Enum.TryParse<FindingCategory>(finding.Category, ignoreCase: true, out var cat)
            ? cat
            : FindingCategory.CodeQuality;

        // Parse severity
        var severity = Enum.TryParse<Severity>(finding.Severity, ignoreCase: true, out var sev)
            ? sev
            : Severity.Medium;

        // Parse line range
        LineRange? lineRange = null;
        if (finding.LineRange is not null && finding.LineRange.Start > 0)
        {
            lineRange = new LineRange(
                finding.LineRange.Start,
                finding.LineRange.End > 0 ? finding.LineRange.End : finding.LineRange.Start);
        }

        return ReviewFinding.Create(
            projectId,
            reportId,
            agentType,
            category,
            severity,
            finding.Description,
            finding.Explanation,
            finding.FilePath,
            null, // FileRecordId - would need lookup
            lineRange,
            finding.SuggestedFix,
            finding.FixedCodeSnippet,
            finding.OriginalCodeSnippet,
            finding.Symbol,
            finding.Confidence);
    }

    private static string GenerateSummary(
        IDictionary<AgentType, AgentAnalysisResponse> responses,
        List<ReviewFinding> findings,
        int healthScore,
        List<string> skippedFindings)
    {
        var sb = new System.Text.StringBuilder();

        // Overall assessment
        var assessment = healthScore switch
        {
            >= 90 => "excellent",
            >= 75 => "good",
            >= 50 => "fair",
            >= 25 => "needs improvement",
            _ => "critical"
        };

        sb.AppendLine($"The overall code health is {assessment} with a score of {healthScore}/100.");
        sb.AppendLine();

        // Summary by severity
        var highCount = findings.Count(f => f.Severity == Severity.High);
        var mediumCount = findings.Count(f => f.Severity == Severity.Medium);
        var lowCount = findings.Count(f => f.Severity == Severity.Low);

        sb.AppendLine($"Total findings: {findings.Count}");
        sb.AppendLine($"- High severity: {highCount}");
        sb.AppendLine($"- Medium severity: {mediumCount}");
        sb.AppendLine($"- Low severity: {lowCount}");
        sb.AppendLine();

        // Agent summaries
        foreach (var (agentType, response) in responses)
        {
            if (!string.IsNullOrEmpty(response.Summary))
            {
                sb.AppendLine($"**{agentType} Analysis**: {response.Summary}");
            }
        }

        // Note skipped findings due to missing evidence
        if (skippedFindings != null && skippedFindings.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("What was not reviewed due to missing evidence:");
            foreach (var s in skippedFindings.Take(20))
            {
                sb.AppendLine($"- {s}");
            }
            if (skippedFindings.Count > 20)
            {
                sb.AppendLine($"- And {skippedFindings.Count - 20} more skipped findings...");
            }
        }

        return sb.ToString();
    }
}

/// <summary>
/// Interface for the report aggregator
/// </summary>
public interface IReportAggregator
{
    Task<Report> AggregateAsync(
        Guid projectId,
        IDictionary<AgentType, AgentAnalysisResponse> agentResponses,
        int analysisDurationSeconds,
        CancellationToken cancellationToken = default);

    int CalculateHealthScore(int highCount, int mediumCount, int lowCount);
}
