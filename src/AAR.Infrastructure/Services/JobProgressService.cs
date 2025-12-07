// =============================================================================
// AAR.Infrastructure - Services/JobProgressService.cs
// Service for reporting job progress to SignalR clients
// =============================================================================

using AAR.Application.DTOs;
using AAR.Application.Interfaces;
using Microsoft.Extensions.Logging;

namespace AAR.Infrastructure.Services;

/// <summary>
/// Reports job progress to SignalR hub and metrics service
/// </summary>
public sealed class JobProgressService : IJobProgressService
{
    private readonly IAnalysisHubNotifier _hubNotifier;
    private readonly IMetricsService _metrics;
    private readonly ILogger<JobProgressService> _logger;

    public JobProgressService(
        IAnalysisHubNotifier hubNotifier,
        IMetricsService metrics,
        ILogger<JobProgressService> logger)
    {
        _hubNotifier = hubNotifier;
        _metrics = metrics;
        _logger = logger;
    }

    public async Task ReportProgressAsync(
        JobProgressUpdate progress, 
        CancellationToken cancellationToken = default)
    {
        // Send to connected clients via SignalR
        await _hubNotifier.SendProgressAsync(progress.ProjectId, progress);

        // Record metrics
        _metrics.RecordGauge(
            $"aar.job.progress.{progress.ProjectId}", 
            progress.ProgressPercent);

        _logger.LogDebug(
            "Job {ProjectId} progress: {Phase} - {Percent:F1}% ({Files}/{TotalFiles} files)",
            progress.ProjectId,
            progress.Phase,
            progress.ProgressPercent,
            progress.FilesProcessed,
            progress.TotalFiles);
    }

    public async Task ReportFindingAsync(
        PartialFindingUpdate finding, 
        CancellationToken cancellationToken = default)
    {
        // Stream partial findings to clients
        await _hubNotifier.SendFindingAsync(finding.ProjectId, finding);

        _logger.LogDebug(
            "Streaming finding for {ProjectId}: {Severity} - {Category}",
            finding.ProjectId,
            finding.Finding.Severity,
            finding.Finding.Category);
    }

    public async Task ReportCompletionAsync(
        JobCompletionUpdate completion, 
        CancellationToken cancellationToken = default)
    {
        // Send completion notification
        await _hubNotifier.SendCompletionAsync(completion.ProjectId, completion);

        // Record metrics
        _metrics.RecordJobCompletion(
            completion.ProjectId,
            "analysis",
            completion.IsSuccess,
            TimeSpan.FromSeconds(completion.ProcessingTimeSeconds));

        _logger.LogInformation(
            "Job {ProjectId} completed: success={Success}, duration={Duration}s, findings={Findings}",
            completion.ProjectId,
            completion.IsSuccess,
            completion.ProcessingTimeSeconds,
            completion.Statistics.FindingsCount);
    }
}
