// =============================================================================
// AAR.Worker - Agents/RagAwareAgentOrchestrator.cs
// RAG-aware agent orchestrator with routing and priority support
// =============================================================================

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
/// Orchestrates analysis agents with RAG-based routing and priority support.
/// </summary>
public class RagAwareAgentOrchestrator : IRagAwareAgentOrchestrator
{
    private readonly IEnumerable<IAnalysisAgent> _agents;
    private readonly IRetrievalOrchestrator _retrievalOrchestrator;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IReportAggregator _reportAggregator;
    private readonly IMemoryMonitor _memoryMonitor;
    private readonly IJobProgressService _progressService;
    private readonly ILogger<RagAwareAgentOrchestrator> _logger;
    private readonly AgentGuardrailOptions _guardrailOptions;

    public RagAwareAgentOrchestrator(
        IEnumerable<IAnalysisAgent> agents,
        IRetrievalOrchestrator retrievalOrchestrator,
        IUnitOfWork unitOfWork,
        IReportAggregator reportAggregator,
        IOptions<AgentGuardrailOptions> guardrailOptions,
        IMemoryMonitor memoryMonitor,
        IJobProgressService progressService,
        ILogger<RagAwareAgentOrchestrator> logger)
    {
        _agents = agents;
        _retrievalOrchestrator = retrievalOrchestrator;
        _unitOfWork = unitOfWork;
        _reportAggregator = reportAggregator;
        _memoryMonitor = memoryMonitor;
        _progressService = progressService;
        _logger = logger;
        _guardrailOptions = guardrailOptions?.Value ?? new AgentGuardrailOptions();
    }

    /// <inheritdoc/>
    public async Task<Report> AnalyzeAsync(
        Guid projectId,
        string workingDirectory,
        CancellationToken cancellationToken = default)
    {
        // Fall back to basic analysis without routing plan
        _logger.LogInformation("Starting basic analysis for project {ProjectId}", projectId);
        
        var stopwatch = Stopwatch.StartNew();
        var allFindings = new List<ReviewFinding>();
        var agentResponses = new Dictionary<AgentType, AgentAnalysisResponse>();

        foreach (var agent in _agents)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                _logger.LogWarning("Analysis cancelled for project {ProjectId}", projectId);
                break;
            }

            // Check memory before each agent
            _memoryMonitor.RecordMemorySample();
            if (_memoryMonitor.ShouldPauseProcessing)
            {
                _logger.LogError("Memory threshold exceeded, pausing analysis");
                throw new InvalidOperationException("Memory threshold exceeded - analysis paused");
            }

            try
            {
                var findings = await agent.AnalyzeAsync(projectId, workingDirectory, cancellationToken);

                // Apply guardrails for this agent
                var filtered = AgentOrchestrator_ApplyGuardrails(agent.AgentType.ToString(), findings, _guardrailOptions);
                allFindings.AddRange(filtered);

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
                    Summary = $"{agent.AgentType} found {filtered.Count} issues",
                    Recommendations = GenerateRecommendationsFromFindings(agent.AgentType, filtered)
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "{AgentType} agent failed", agent.AgentType);
                agentResponses[agent.AgentType] = new AgentAnalysisResponse
                {
                    Findings = new List<AgentFinding>(),
                    Summary = $"{agent.AgentType} failed: {ex.Message}",
                    Recommendations = new List<string>()
                };
            }
        }

        stopwatch.Stop();
        return await _reportAggregator.AggregateAsync(
            projectId, agentResponses, (int)stopwatch.Elapsed.TotalSeconds, cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<Report> AnalyzeWithPlanAsync(
        ProjectAnalysisPlan plan,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "Starting RAG-aware analysis for project {ProjectId}: {DirectSend} direct, {RagChunk} RAG, {Skipped} skipped",
            plan.ProjectId, plan.DirectSendCount, plan.RagChunkCount, plan.SkippedCount);

        var stopwatch = Stopwatch.StartNew();
        var allFindings = new List<ReviewFinding>();
        var agentResponses = new Dictionary<AgentType, AgentAnalysisResponse>();

        // Log the plan
        LogAnalysisPlan(plan);

        // Save plan to checkpoint state
        await SavePlanToCheckpointAsync(plan, cancellationToken);

        // Process high-risk files first
        var prioritizedFiles = plan.HighRiskFiles.Concat(plan.NormalPriorityFiles).ToList();
        var totalFiles = prioritizedFiles.Count;
        var processedFiles = 0;

        _logger.LogInformation(
            "Processing {TotalFiles} files ({HighRisk} high-risk first)",
            totalFiles, plan.HighRiskFiles.Count);

        foreach (var agent in _agents)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                _logger.LogWarning("Analysis cancelled for project {ProjectId}", plan.ProjectId);
                break;
            }

            // Check memory before each agent
            _memoryMonitor.RecordMemorySample();
            if (_memoryMonitor.ShouldPauseProcessing)
            {
                _logger.LogError("Memory threshold exceeded, saving checkpoint and pausing");
                await SaveCheckpointAsync(plan.ProjectId, processedFiles, "paused_on_resource", cancellationToken);
                throw new InvalidOperationException("Memory threshold exceeded - analysis paused. Job can be resumed.");
            }

            var agentStopwatch = Stopwatch.StartNew();
            _logger.LogInformation("Running {AgentType} agent with routing plan", agent.AgentType);

            try
            {
                // Standard analysis - agents process files based on plan
                var findings = await agent.AnalyzeAsync(
                    plan.ProjectId, plan.WorkingDirectory, cancellationToken);

                agentStopwatch.Stop();

                // Apply guardrails to findings produced under the routing plan
                var filtered = AgentOrchestrator_ApplyGuardrails(agent.AgentType.ToString(), findings, _guardrailOptions);

                allFindings.AddRange(filtered);

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
                        SuggestedFix = f.SuggestedFix
                    }).ToList(),
                    Summary = $"{agent.AgentType} found {filtered.Count} issues",
                    Recommendations = GenerateRecommendationsFromFindings(agent.AgentType, filtered)
                };

                _logger.LogInformation(
                    "{AgentType} completed: {FindingsCount} findings in {Duration}ms",
                    agent.AgentType, findings.Count, agentStopwatch.ElapsedMilliseconds);

                // Report progress with correct agent type
                await _progressService.ReportProgressAsync(new JobProgressUpdate
                {
                    ProjectId = plan.ProjectId,
                    Phase = "Analyzing",
                    ProgressPercent = 40 + (int)(40.0 * processedFiles / Math.Max(1, totalFiles)),
                    CurrentFile = agent.AgentType.ToString(),
                    FilesProcessed = processedFiles,
                    TotalFiles = totalFiles
                }, cancellationToken);
            }
            catch (Exception ex)
            {
                agentStopwatch.Stop();
                _logger.LogError(ex, "{AgentType} agent failed", agent.AgentType);

                agentResponses[agent.AgentType] = new AgentAnalysisResponse
                {
                    Findings = new List<AgentFinding>(),
                    Summary = $"{agent.AgentType} failed: {ex.Message}",
                    Recommendations = new List<string>()
                };

                // Add error finding
                allFindings.Add(ReviewFinding.Create(
                    projectId: plan.ProjectId,
                    reportId: Guid.Empty,
                    agentType: agent.AgentType,
                    category: FindingCategory.Other,
                    severity: Severity.Info,
                    description: $"{agent.AgentType} Agent Error",
                    explanation: $"Agent failed with error: {ex.Message}"));
            }

            // Cleanup after each agent
            _memoryMonitor.RequestGCIfNeeded();
        }

        // Add skipped files to report
        AddSkippedFilesFindings(plan, allFindings);

        stopwatch.Stop();

        _logger.LogInformation(
            "Analysis completed: {TotalFindings} findings in {Duration}ms",
            allFindings.Count, stopwatch.ElapsedMilliseconds);

        return await _reportAggregator.AggregateAsync(
            plan.ProjectId, agentResponses, (int)stopwatch.Elapsed.TotalSeconds, cancellationToken);
    }

    private void LogAnalysisPlan(ProjectAnalysisPlan plan)
    {
        _logger.LogInformation("=== Analysis Plan ===");
        _logger.LogInformation("Project: {ProjectId}", plan.ProjectId);
        _logger.LogInformation("Total files: {TotalFiles}", plan.Files.Count);
        _logger.LogInformation("Direct send: {DirectSend}", plan.DirectSendCount);
        _logger.LogInformation("RAG chunks: {RagChunks}", plan.RagChunkCount);
        _logger.LogInformation("Skipped: {Skipped}", plan.SkippedCount);
        _logger.LogInformation("High-risk files: {HighRisk}", plan.HighRiskFiles.Count);
        _logger.LogInformation("Est. tokens: {Tokens:N0}", plan.EstimatedTotalTokens);

        // Log high-risk files
        foreach (var file in plan.HighRiskFiles.Take(10))
        {
            _logger.LogInformation("  [HIGH-RISK] {File} (score: {Score:F2})", 
                file.FilePath, file.RiskScore);
        }

        // Log skipped files
        foreach (var file in plan.Files.Where(f => f.Decision == FileRoutingDecision.Skipped).Take(10))
        {
            _logger.LogDebug("  [SKIPPED] {File}: {Reason}", file.FilePath, file.DecisionReason);
        }
    }

    private void AddSkippedFilesFindings(ProjectAnalysisPlan plan, List<ReviewFinding> findings)
    {
        var skippedLargeFiles = plan.Files
            .Where(f => f.Decision == FileRoutingDecision.Skipped && 
                        f.DecisionReason == SkipReasonCodes.TooLarge)
            .ToList();

        if (skippedLargeFiles.Count > 0)
        {
            findings.Add(ReviewFinding.Create(
                projectId: plan.ProjectId,
                reportId: Guid.Empty,
                agentType: AgentType.Structure,
                category: FindingCategory.Other,
                severity: Severity.Info,
                description: $"Skipped {skippedLargeFiles.Count} large files",
                explanation: $"The following files were skipped because they exceed the size threshold: " +
                    string.Join(", ", skippedLargeFiles.Take(10).Select(f => f.FilePath)) +
                    (skippedLargeFiles.Count > 10 ? $" and {skippedLargeFiles.Count - 10} more" : "")));
        }
    }

    private async Task SavePlanToCheckpointAsync(ProjectAnalysisPlan plan, CancellationToken cancellationToken)
    {
        try
        {
            var checkpoint = await _unitOfWork.JobCheckpoints.GetByProjectIdAsync(plan.ProjectId, cancellationToken);
            if (checkpoint != null)
            {
                checkpoint.SetSerializedState(plan.ToJson());
                await _unitOfWork.SaveChangesAsync(cancellationToken);
                _logger.LogDebug("Saved analysis plan to checkpoint");
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to save plan to checkpoint");
        }
    }

    private async Task SaveCheckpointAsync(
        Guid projectId, 
        int filesProcessed, 
        string status, 
        CancellationToken cancellationToken)
    {
        try
        {
            var checkpoint = await _unitOfWork.JobCheckpoints.GetByProjectIdAsync(projectId, cancellationToken);
            if (checkpoint != null)
            {
                checkpoint.UpdateProgress(
                    ProcessingPhase.Analyzing,
                    filesProcessed,
                    filesProcessed,
                    0, 0, 0);
                
                if (status == "paused_on_resource")
                {
                    checkpoint.MarkFailed("Paused due to memory threshold");
                }

                await _unitOfWork.SaveChangesAsync(cancellationToken);
                _logger.LogInformation("Saved checkpoint at file {FileIndex}", filesProcessed);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save checkpoint");
        }
    }

    /// <summary>
    /// Generates recommendations based on the findings from an agent.
    /// </summary>
    private static List<string> GenerateRecommendationsFromFindings(AgentType agentType, List<ReviewFinding> findings)
    {
        var recommendations = new List<string>();

        // Count findings by severity
        var highCount = findings.Count(f => f.Severity == Severity.High || f.Severity == Severity.Critical);
        var mediumCount = findings.Count(f => f.Severity == Severity.Medium);
        var lowCount = findings.Count(f => f.Severity == Severity.Low);

        // Count findings by category
        var categoryCounts = findings
            .GroupBy(f => f.Category)
            .ToDictionary(g => g.Key, g => g.Count());

        // Generate recommendations based on agent type and findings
        switch (agentType)
        {
            case AgentType.Security:
                if (highCount > 0)
                    recommendations.Add($"Address {highCount} high-severity security issue(s) immediately to reduce risk.");
                if (categoryCounts.ContainsKey(FindingCategory.Security) && categoryCounts[FindingCategory.Security] > 2)
                    recommendations.Add("Consider implementing a security code review process to catch vulnerabilities early.");
                if (findings.Any(f => f.Description.Contains("injection", StringComparison.OrdinalIgnoreCase)))
                    recommendations.Add("Use parameterized queries and input validation to prevent injection attacks.");
                if (findings.Any(f => f.Description.Contains("authentication", StringComparison.OrdinalIgnoreCase) || 
                                      f.Description.Contains("password", StringComparison.OrdinalIgnoreCase)))
                    recommendations.Add("Review authentication mechanisms and ensure secure password handling.");
                break;

            case AgentType.CodeQuality:
                if (findings.Count > 10)
                    recommendations.Add("Consider refactoring to reduce code complexity and improve maintainability.");
                if (categoryCounts.ContainsKey(FindingCategory.Performance))
                    recommendations.Add("Profile the application to identify and optimize performance bottlenecks.");
                if (categoryCounts.ContainsKey(FindingCategory.Maintainability) && categoryCounts[FindingCategory.Maintainability] > 3)
                    recommendations.Add("Improve code documentation and add unit tests to enhance maintainability.");
                if (findings.Any(f => f.Description.Contains("duplication", StringComparison.OrdinalIgnoreCase)))
                    recommendations.Add("Extract common code into reusable functions or classes to reduce duplication.");
                break;

            case AgentType.ArchitectureAdvisor:
                if (findings.Count > 0)
                    recommendations.Add("Review the overall architecture to ensure proper separation of concerns.");
                if (categoryCounts.ContainsKey(FindingCategory.Architecture) && categoryCounts[FindingCategory.Architecture] > 2)
                    recommendations.Add("Consider adopting established design patterns to improve code organization.");
                if (findings.Any(f => f.Description.Contains("dependency", StringComparison.OrdinalIgnoreCase) || 
                                      f.Description.Contains("coupling", StringComparison.OrdinalIgnoreCase)))
                    recommendations.Add("Reduce coupling between components using dependency injection and interfaces.");
                break;

            case AgentType.Structure:
                if (findings.Count > 0)
                    recommendations.Add("Organize project files according to feature or layer-based structure.");
                if (findings.Any(f => f.Description.Contains("naming", StringComparison.OrdinalIgnoreCase)))
                    recommendations.Add("Establish and follow consistent naming conventions across the codebase.");
                break;
        }

        // Generic recommendations based on severity
        if (highCount > 5)
            recommendations.Add("Prioritize fixing high-severity issues before adding new features.");
        if (mediumCount > 10)
            recommendations.Add("Schedule time to address medium-severity issues to prevent technical debt accumulation.");
        if (findings.Count == 0)
            recommendations.Add($"No issues found by {agentType} agent. Keep up the good work!");

        return recommendations.Take(5).ToList(); // Limit to 5 recommendations per agent
    }

    private static List<ReviewFinding> AgentOrchestrator_ApplyGuardrails(string agentName, List<ReviewFinding> findings, AgentGuardrailOptions options)
    {
        if (findings == null || findings.Count == 0) return new List<ReviewFinding>();

        options = options ?? new AgentGuardrailOptions();

        if (!options.AgentLimits.TryGetValue(agentName, out var limits))
        {
            return findings;
        }

        var filtered = findings.Where(f => f.Confidence >= limits.MinConfidence).ToList();

        if (limits.AllowedCategories != null && limits.AllowedCategories.Count > 0)
        {
            filtered = filtered.Where(f => limits.AllowedCategories.Contains(f.Category)).ToList();
        }

        var deduped = filtered
            .GroupBy(f => (f.FilePath ?? string.Empty, f.Symbol ?? string.Empty, f.Description ?? string.Empty))
            .Select(g => g.OrderByDescending(x => x.Confidence).First())
            .ToList();

        if (limits.MaxFindings > 0 && deduped.Count > limits.MaxFindings)
        {
            deduped = deduped.OrderByDescending(f => f.Confidence).Take(limits.MaxFindings).ToList();
        }

        return deduped;
    }
}
