// =============================================================================
// AAR.Infrastructure - Services/Watchdog/BatchProcessingWatchdog.cs
// Watchdog service to detect and recover from stuck batch processing
// =============================================================================

using AAR.Application.Interfaces;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Collections.Concurrent;

namespace AAR.Infrastructure.Services.Watchdog;

/// <summary>
/// Configuration for the batch processing watchdog
/// </summary>
public class WatchdogOptions
{
    /// <summary>
    /// Whether the watchdog is enabled. Default: true
    /// </summary>
    public bool Enabled { get; set; } = true;
    
    /// <summary>
    /// How often to check for stuck batches. Default: 30 seconds
    /// </summary>
    public int CheckIntervalSeconds { get; set; } = 30;
    
    /// <summary>
    /// Maximum time a project indexing can run before being considered stuck. Default: 10 minutes
    /// </summary>
    public int MaxProjectDurationSeconds { get; set; } = 600;
    
    /// <summary>
    /// Maximum time without a heartbeat before considered stuck. Default: 2 minutes
    /// </summary>
    public int MaxHeartbeatIntervalSeconds { get; set; } = 120;
    
    /// <summary>
    /// Whether to automatically cancel stuck operations. Default: true
    /// </summary>
    public bool AutoCancelStuck { get; set; } = true;
    
    /// <summary>
    /// Number of stuck detections before forcing cancellation. Default: 2
    /// </summary>
    public int StuckDetectionThreshold { get; set; } = 2;
}

/// <summary>
/// Represents a tracked project indexing operation
/// </summary>
public class ProjectIndexingInfo
{
    public required Guid ProjectId { get; init; }
    public required int TotalBatches { get; init; }
    public required DateTime StartedAt { get; init; }
    public int CurrentBatch { get; set; }
    public string CurrentPhase { get; set; } = "Starting";
    public DateTime LastHeartbeat { get; set; }
    public int StuckDetectionCount { get; set; }
    public CancellationTokenSource? LinkedCts { get; set; }
}

/// <summary>
/// Interface for batch processing watchdog
/// </summary>
public interface IBatchProcessingWatchdog
{
    /// <summary>
    /// Register a project indexing operation for monitoring.
    /// Returns a linked CancellationTokenSource that can be cancelled by the watchdog.
    /// </summary>
    CancellationTokenSource TrackBatch(Guid projectId, int batchNumber, int totalBatches, CancellationToken externalToken);
    
    /// <summary>
    /// Update the current phase of a project's indexing
    /// </summary>
    void UpdatePhase(Guid projectId, string phase);
    
    /// <summary>
    /// Send a heartbeat for a project
    /// </summary>
    void Heartbeat(Guid projectId);
    
    /// <summary>
    /// Mark project indexing as complete and stop tracking
    /// </summary>
    void Complete(Guid projectId);
    
    /// <summary>
    /// Get current status of all tracked projects
    /// </summary>
    IReadOnlyList<ProjectIndexingInfo> GetTrackedProjects();
    
    /// <summary>
    /// Check if a specific project is stuck
    /// </summary>
    bool IsProjectStuck(Guid projectId);
}

/// <summary>
/// Watchdog service that monitors batch processing and detects/recovers from stuck states
/// </summary>
public class BatchProcessingWatchdog : BackgroundService, IBatchProcessingWatchdog
{
    private readonly ConcurrentDictionary<Guid, ProjectIndexingInfo> _trackedProjects = new();
    private readonly WatchdogOptions _options;
    private readonly ILogger<BatchProcessingWatchdog> _logger;
    private readonly IMetricsService? _metrics;

    public BatchProcessingWatchdog(
        IOptions<WatchdogOptions> options,
        ILogger<BatchProcessingWatchdog> logger,
        IMetricsService? metrics = null)
    {
        _options = options.Value;
        _logger = logger;
        _metrics = metrics;
    }

    public CancellationTokenSource TrackBatch(Guid projectId, int batchNumber, int totalBatches, CancellationToken externalToken)
    {
        // Create a linked CTS that can be cancelled externally OR by the watchdog
        var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(externalToken);
        
        var info = new ProjectIndexingInfo
        {
            ProjectId = projectId,
            TotalBatches = totalBatches,
            StartedAt = DateTime.UtcNow,
            CurrentBatch = batchNumber,
            CurrentPhase = "Starting",
            LastHeartbeat = DateTime.UtcNow,
            LinkedCts = linkedCts
        };
        
        _trackedProjects[projectId] = info;
        
        _logger.LogDebug("Watchdog: Tracking project {ProjectId} with {Total} batches",
            projectId, totalBatches);
        
        return linkedCts;
    }

    public void UpdatePhase(Guid projectId, string phase)
    {
        if (_trackedProjects.TryGetValue(projectId, out var info))
        {
            info.CurrentPhase = phase;
            info.LastHeartbeat = DateTime.UtcNow;
            _logger.LogTrace("Watchdog: Project {ProjectId} phase updated to {Phase}", projectId, phase);
        }
    }

    public void Heartbeat(Guid projectId)
    {
        if (_trackedProjects.TryGetValue(projectId, out var info))
        {
            info.LastHeartbeat = DateTime.UtcNow;
            info.StuckDetectionCount = 0; // Reset stuck counter on heartbeat
        }
    }

    public void Complete(Guid projectId)
    {
        if (_trackedProjects.TryRemove(projectId, out var info))
        {
            var duration = DateTime.UtcNow - info.StartedAt;
            _logger.LogInformation("Watchdog: Project {ProjectId} completed in {Duration:F1}s ({Batches} batches)",
                projectId, duration.TotalSeconds, info.TotalBatches);
            
            _metrics?.RecordHistogram("indexing_duration_seconds", duration.TotalSeconds);
        }
    }

    public IReadOnlyList<ProjectIndexingInfo> GetTrackedProjects()
    {
        return _trackedProjects.Values.ToList();
    }

    public bool IsProjectStuck(Guid projectId)
    {
        if (!_trackedProjects.TryGetValue(projectId, out var info))
            return false;
        
        var elapsed = DateTime.UtcNow - info.StartedAt;
        var sincLastHeartbeat = DateTime.UtcNow - info.LastHeartbeat;
        
        return elapsed.TotalSeconds > _options.MaxProjectDurationSeconds ||
               sincLastHeartbeat.TotalSeconds > _options.MaxHeartbeatIntervalSeconds;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled)
        {
            _logger.LogInformation("Watchdog is disabled");
            return;
        }

        _logger.LogInformation("Watchdog started with check interval of {Interval}s", 
            _options.CheckIntervalSeconds);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(_options.CheckIntervalSeconds), stoppingToken);
                await CheckForStuckProjectsAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Watchdog check failed");
            }
        }

        _logger.LogInformation("Watchdog stopped");
    }

    private Task CheckForStuckProjectsAsync(CancellationToken stoppingToken)
    {
        var now = DateTime.UtcNow;
        var stuckProjects = new List<(Guid ProjectId, ProjectIndexingInfo Info, string Reason)>();

        foreach (var kvp in _trackedProjects)
        {
            var projectId = kvp.Key;
            var info = kvp.Value;
            var elapsed = now - info.StartedAt;
            var sincLastHeartbeat = now - info.LastHeartbeat;

            // Check if project is stuck
            string? reason = null;

            if (elapsed.TotalSeconds > _options.MaxProjectDurationSeconds)
            {
                reason = $"exceeded max duration ({elapsed.TotalSeconds:F0}s > {_options.MaxProjectDurationSeconds}s)";
            }
            else if (sincLastHeartbeat.TotalSeconds > _options.MaxHeartbeatIntervalSeconds)
            {
                reason = $"no heartbeat for {sincLastHeartbeat.TotalSeconds:F0}s (max {_options.MaxHeartbeatIntervalSeconds}s)";
            }

            if (reason != null)
            {
                info.StuckDetectionCount++;
                stuckProjects.Add((projectId, info, reason));

                _logger.LogWarning(
                    "Watchdog: Project {ProjectId} appears STUCK: {Reason}. " +
                    "Batch: {Batch}/{Total}, Phase: {Phase}, Detection count: {Count}",
                    projectId, reason,
                    info.CurrentBatch, info.TotalBatches, info.CurrentPhase, info.StuckDetectionCount);

                _metrics?.IncrementCounter("watchdog_stuck_detected");
            }
        }

        // Handle stuck projects
        if (_options.AutoCancelStuck)
        {
            foreach (var (projectId, info, reason) in stuckProjects.Where(x => 
                x.Info.StuckDetectionCount >= _options.StuckDetectionThreshold))
            {
                _logger.LogError(
                    "Watchdog: CANCELLING stuck project {ProjectId}. " +
                    "Stuck detection count reached threshold ({Count} >= {Threshold}). Reason: {Reason}",
                    projectId, info.StuckDetectionCount, _options.StuckDetectionThreshold, reason);

                try
                {
                    // Cancel the operation
                    info.LinkedCts?.Cancel();
                    
                    // Remove from tracking
                    _trackedProjects.TryRemove(projectId, out _);
                    
                    _metrics?.IncrementCounter("watchdog_project_cancelled");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to cancel stuck project {ProjectId}", projectId);
                }
            }
        }

        return Task.CompletedTask;
    }
}
