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
    private readonly AAR.Application.Interfaces.IOpenAiService _openAiService;

    public ReportAggregator(
        IUnitOfWork unitOfWork,
        ILogger<ReportAggregator> logger,
        AAR.Application.Interfaces.IOpenAiService openAiService)
    {
        _unitOfWork = unitOfWork;
        _logger = logger;
        _openAiService = openAiService;
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
                // Basic validation: require at least a description and either:
                // - A file path (file-level finding), OR
                // - A meaningful explanation (project-level finding)
                var hasDescription = !string.IsNullOrWhiteSpace(finding.Description);
                var hasFile = !string.IsNullOrWhiteSpace(finding.FilePath);
                var hasExplanation = !string.IsNullOrWhiteSpace(finding.Explanation);

                // Reject findings that are completely empty or have no useful context
                if (!hasDescription)
                {
                    _logger.LogWarning("Skipping finding from {AgentType} due to missing description",
                        agentType);
                    skippedFindings.Add($"{agentType}: [no description]");
                    continue;
                }

                // For project-level findings (no file), require explanation
                if (!hasFile && !hasExplanation)
                {
                    _logger.LogWarning(
                        "Skipping project-level finding from {AgentType} - no file and no explanation: {Description}",
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

        // Prefer AI-generated recommendations when available
        var uniqueRecommendations = new List<string>();

        try
        {
            var context = new AAR.Application.DTOs.AnalysisContext
            {
                ProjectId = projectId,
                ProjectName = string.Empty,
                WorkingDirectory = string.Empty,
                Files = new List<AAR.Application.DTOs.AnalysisFileInfo>()
            };

            var summaryResult = await _openAiService.GenerateSummaryAsync(context, agentResponses.Values, cancellationToken);
            if (summaryResult.IsSuccess && !string.IsNullOrWhiteSpace(summaryResult.Value))
            {
                try
                {
                    using var doc = System.Text.Json.JsonDocument.Parse(summaryResult.Value);
                    var root = doc.RootElement;

                    // If LLM returned recommendations, use them
                    if (root.TryGetProperty("recommendations", out var recs) && recs.ValueKind == System.Text.Json.JsonValueKind.Array)
                    {
                        uniqueRecommendations = recs.EnumerateArray()
                            .Where(e => e.ValueKind == System.Text.Json.JsonValueKind.String)
                            .Select(e => e.GetString()!)
                            .Distinct()
                            .Take(10)
                            .ToList();
                    }

                    // If LLM returned a summary, prefer it
                    if (root.TryGetProperty("summary", out var llmSummary) && llmSummary.ValueKind == System.Text.Json.JsonValueKind.String)
                    {
                        // Override generated summary
                        var llmSummaryText = llmSummary.GetString();
                        if (!string.IsNullOrWhiteSpace(llmSummaryText))
                        {
                            summary = llmSummaryText;
                        }
                    }
                }
                catch (System.Text.Json.JsonException ex)
                {
                    _logger.LogWarning(ex, "Failed to parse LLM summary JSON; falling back to static recommendations");
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "LLM summary generation failed; falling back to static recommendations");
        }

        if (uniqueRecommendations == null || uniqueRecommendations.Count == 0)
        {
            // Deduplicate and limit recommendations from agents/heuristics
            uniqueRecommendations = allRecommendations
                .Distinct()
                .Take(10)
                .ToList();
        }

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
