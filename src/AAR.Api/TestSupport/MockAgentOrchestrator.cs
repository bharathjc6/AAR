// =============================================================================
// AAR.Api - TestSupport/MockAgentOrchestrator.cs
// Mock agent orchestrator for integrated testing mode
// =============================================================================

using AAR.Application.Interfaces;
using AAR.Domain.Entities;
using AAR.Domain.Enums;
using Microsoft.Extensions.Logging;

namespace AAR.Api.TestSupport;

/// <summary>
/// Mock agent orchestrator for local testing without real OpenAI calls
/// </summary>
public class MockAgentOrchestrator : IAgentOrchestrator
{
    private readonly ILogger<MockAgentOrchestrator> _logger;

    public MockAgentOrchestrator(ILogger<MockAgentOrchestrator> logger)
    {
        _logger = logger;
    }

    public async Task<Report> AnalyzeAsync(
        Guid projectId, 
        string workingDirectory, 
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "Mock analysis starting for project {ProjectId} in {Directory}", 
            projectId, workingDirectory);

        // Simulate some processing time
        await Task.Delay(1000, cancellationToken);

        // Count files in the directory
        var files = Directory.Exists(workingDirectory) 
            ? Directory.GetFiles(workingDirectory, "*", SearchOption.AllDirectories)
            : Array.Empty<string>();
        
        var fileCount = files.Length;
        var totalLines = 0;
        
        foreach (var file in files)
        {
            try
            {
                totalLines += File.ReadAllLines(file).Length;
            }
            catch
            {
                // Ignore unreadable files
            }
        }

        _logger.LogInformation(
            "Mock analysis found {FileCount} files with {TotalLines} lines", 
            fileCount, totalLines);

        // Create the report using factory method
        var report = Report.Create(projectId);

        // Update statistics
        var recommendations = new List<string>
        {
            "Continue following current coding standards",
            "Consider adding more documentation",
            "Review test coverage for critical paths"
        };

        report.UpdateStatistics(
            summary: $"Mock analysis completed for project. Analyzed {fileCount} files with {totalLines} total lines of code.",
            recommendations: recommendations,
            healthScore: 85,
            highCount: 0,
            mediumCount: 1,
            lowCount: 2,
            durationSeconds: 1
        );

        _logger.LogInformation(
            "Mock analysis completed for project {ProjectId} with score {Score}", 
            projectId, report.HealthScore);

        return report;
    }
}
