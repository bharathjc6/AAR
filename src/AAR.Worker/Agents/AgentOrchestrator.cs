using System.Diagnostics;
using AAR.Application.DTOs;
using AAR.Application.Interfaces;
using AAR.Application.Services;
using AAR.Domain.Entities;
using AAR.Domain.Enums;
using AAR.Domain.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using AAR.Application.Configuration;

namespace AAR.Worker.Agents;

/// <summary>
/// Orchestrates all analysis agents and produces a consolidated report.
/// </summary>
public class AgentOrchestrator : IAgentOrchestrator
{
    private readonly IEnumerable<IAnalysisAgent> _agents;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IReportAggregator _reportAggregator;
    private readonly ILogger<AgentOrchestrator> _logger;
    private readonly AgentGuardrailOptions _guardrailOptions;

    public AgentOrchestrator(
        IEnumerable<IAnalysisAgent> agents,
        IUnitOfWork unitOfWork,
        IReportAggregator reportAggregator,
        IOptions<AgentGuardrailOptions> guardrailOptions,
        ILogger<AgentOrchestrator> logger)
    {
        _agents = agents;
        _unitOfWork = unitOfWork;
        _reportAggregator = reportAggregator;
        _logger = logger;
        _guardrailOptions = guardrailOptions?.Value ?? new AgentGuardrailOptions();
    }

    public async Task<Report> AnalyzeAsync(
        Guid projectId,
        string workingDirectory,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Starting orchestrated analysis for project {ProjectId}", projectId);
        
        var stopwatch = Stopwatch.StartNew();
        var allFindings = new List<ReviewFinding>();
        var agentResults = new Dictionary<AgentType, AgentResult>();
        var agentResponses = new Dictionary<AgentType, AgentAnalysisResponse>();
        
        // Run each agent
        foreach (var agent in _agents)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                _logger.LogWarning("Analysis cancelled for project {ProjectId}", projectId);
                break;
            }
            
            _logger.LogInformation("Running {AgentType} agent", agent.AgentType);
            var agentStopwatch = Stopwatch.StartNew();
            
            try
            {
                var findings = await agent.AnalyzeAsync(projectId, workingDirectory, cancellationToken);
                agentStopwatch.Stop();
                // Apply guardrails: per-agent allowed categories, min confidence and limits
                var filtered = ApplyGuardrails(agent.AgentType.ToString(), findings, _guardrailOptions);
                allFindings.AddRange(filtered);
                
                agentResults[agent.AgentType] = new AgentResult
                {
                    AgentType = agent.AgentType,
                    FindingsCount = findings.Count,
                    DurationMs = agentStopwatch.ElapsedMilliseconds,
                    Success = true
                };

                // Create an agent response for aggregation
                agentResponses[agent.AgentType] = new AgentAnalysisResponse
                {
                    Findings = filtered.Select(f => new AgentFinding
                    {
                        Id = f.Id.ToString(),
                        Description = f.Description,
                        Explanation = f.Explanation,
                        Severity = f.Severity.ToString(),
                        Category = f.Category.ToString(),
                        FilePath = f.FilePath,
                        SuggestedFix = f.SuggestedFix,
                        Symbol = f.Symbol,
                        Confidence = f.Confidence
                    }).ToList(),
                    Summary = $"{agent.AgentType} found {findings.Count} issues",
                    Recommendations = new List<string>()
                };
                
                _logger.LogInformation(
                    "{AgentType} agent completed: {FindingsCount} findings in {Duration}ms",
                    agent.AgentType, findings.Count, agentStopwatch.ElapsedMilliseconds);
            }
            catch (Exception ex)
            {
                agentStopwatch.Stop();
                
                _logger.LogError(ex, "{AgentType} agent failed", agent.AgentType);
                
                agentResults[agent.AgentType] = new AgentResult
                {
                    AgentType = agent.AgentType,
                    FindingsCount = 0,
                    DurationMs = agentStopwatch.ElapsedMilliseconds,
                    Success = false,
                    Error = ex.Message
                };
                
                // Add an error finding using the factory method
                var errorFinding = ReviewFinding.Create(
                    projectId: projectId,
                    reportId: Guid.Empty, // Will be updated when report is created
                    agentType: agent.AgentType,
                    category: FindingCategory.Other,
                    severity: Severity.Info,
                    description: $"{agent.AgentType} Agent Error",
                    explanation: $"Agent failed with error: {ex.Message}");
                allFindings.Add(errorFinding);
            }
        }
        
        stopwatch.Stop();
        
        _logger.LogInformation(
            "All agents completed. Total findings: {TotalFindings}, Duration: {Duration}ms",
            allFindings.Count, stopwatch.ElapsedMilliseconds);
        
        // Create the report using the aggregator
        var report = await _reportAggregator.AggregateAsync(
            projectId, 
            agentResponses, 
            (int)stopwatch.Elapsed.TotalSeconds,
            cancellationToken);
        
        _logger.LogInformation(
            "Report generated for project {ProjectId}. Health score: {Score}",
            projectId, _reportAggregator.CalculateHealthScore(
                allFindings.Count(f => f.Severity == Severity.High),
                allFindings.Count(f => f.Severity == Severity.Medium),
                allFindings.Count(f => f.Severity == Severity.Low)));
        
        return report;
    }

    private class AgentResult
    {
        public AgentType AgentType { get; init; }
        public int FindingsCount { get; init; }
        public long DurationMs { get; init; }
        public bool Success { get; init; }
        public string? Error { get; init; }
    }

    private static List<ReviewFinding> ApplyGuardrails(string agentName, List<ReviewFinding> findings, AgentGuardrailOptions options)
    {
        if (findings == null || findings.Count == 0) return new List<ReviewFinding>();

        options = options ?? new AgentGuardrailOptions();

        if (!options.AgentLimits.TryGetValue(agentName, out var limits))
        {
            // No per-agent limits configured - return as-is
            return findings;
        }

        // Filter by min confidence
        var filtered = findings.Where(f => f.Confidence >= limits.MinConfidence).ToList();

        // Filter by allowed categories if configured
        if (limits.AllowedCategories != null && limits.AllowedCategories.Count > 0)
        {
            filtered = filtered.Where(f => limits.AllowedCategories.Contains(f.Category)).ToList();
        }

        // Deduplicate by FilePath+Symbol+Description, keep highest confidence
        var deduped = filtered
            .GroupBy(f => (f.FilePath ?? string.Empty, f.Symbol ?? string.Empty, f.Description ?? string.Empty))
            .Select(g => g.OrderByDescending(x => x.Confidence).First())
            .ToList();

        // Limit count
        if (limits.MaxFindings > 0 && deduped.Count > limits.MaxFindings)
        {
            deduped = deduped.OrderByDescending(f => f.Confidence).Take(limits.MaxFindings).ToList();
        }

        return deduped;
    }
}
