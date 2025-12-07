// =============================================================================
// AAR.Infrastructure - Services/Telemetry/InMemoryMetricsService.cs
// In-memory metrics implementation (for dev, swap with App Insights for production)
// =============================================================================

using System.Collections.Concurrent;
using System.Diagnostics;
using AAR.Application.Interfaces;
using Microsoft.Extensions.Logging;

namespace AAR.Infrastructure.Services.Telemetry;

/// <summary>
/// In-memory metrics service for development/testing
/// For production, integrate with Application Insights or Prometheus
/// </summary>
public sealed class InMemoryMetricsService : IMetricsService
{
    private readonly ILogger<InMemoryMetricsService> _logger;
    private readonly ConcurrentDictionary<string, long> _counters = new();
    private readonly ConcurrentDictionary<string, double> _gauges = new();
    private readonly ConcurrentDictionary<string, List<double>> _histograms = new();
    private readonly ConcurrentDictionary<Guid, DateTime> _activeJobs = new();
    private readonly List<JobCompletionRecord> _completedJobs = new();
    private readonly object _lock = new();

    // Rolling window data
    private readonly ConcurrentQueue<TokenRecord> _tokenRecords = new();
    private readonly ConcurrentQueue<JobRecord> _jobRecords = new();

    public InMemoryMetricsService(ILogger<InMemoryMetricsService> logger)
    {
        _logger = logger;
    }

    public void IncrementCounter(string name, IDictionary<string, string>? tags = null)
    {
        _counters.AddOrUpdate(name, 1, (_, v) => v + 1);
        _logger.LogTrace("Counter {Name}: {Value}", name, _counters[name]);
    }

    public void RecordGauge(string name, double value, IDictionary<string, string>? tags = null)
    {
        _gauges[name] = value;
        _logger.LogTrace("Gauge {Name}: {Value}", name, value);
    }

    public void RecordHistogram(string name, double value, IDictionary<string, string>? tags = null)
    {
        var list = _histograms.GetOrAdd(name, _ => new List<double>());
        lock (list)
        {
            list.Add(value);
            // Keep only last 1000 values
            if (list.Count > 1000)
                list.RemoveAt(0);
        }
        _logger.LogTrace("Histogram {Name}: {Value}", name, value);
    }

    public IDisposable StartTimer(string name, IDictionary<string, string>? tags = null)
    {
        return new TimerScope(this, name, tags);
    }

    public void RecordJobStart(Guid projectId, string jobType)
    {
        _activeJobs[projectId] = DateTime.UtcNow;
        IncrementCounter(MetricNames.JobsQueued);
        _logger.LogInformation("Job started: {ProjectId} ({JobType})", projectId, jobType);
    }

    public void RecordJobCompletion(Guid projectId, string jobType, bool success, TimeSpan duration)
    {
        _activeJobs.TryRemove(projectId, out _);

        lock (_lock)
        {
            _completedJobs.Add(new JobCompletionRecord
            {
                ProjectId = projectId,
                JobType = jobType,
                Success = success,
                Duration = duration,
                CompletedAt = DateTime.UtcNow
            });

            // Keep only last 1000 jobs
            if (_completedJobs.Count > 1000)
                _completedJobs.RemoveAt(0);
        }

        _jobRecords.Enqueue(new JobRecord
        {
            Timestamp = DateTime.UtcNow,
            Duration = duration,
            Success = success
        });

        // Trim old records
        while (_jobRecords.Count > 1000)
            _jobRecords.TryDequeue(out _);

        RecordHistogram(MetricNames.JobDuration, duration.TotalSeconds);
        IncrementCounter(success ? MetricNames.JobsCompleted : MetricNames.JobsFailed);

        _logger.LogInformation(
            "Job completed: {ProjectId} ({JobType}), success={Success}, duration={Duration}",
            projectId, jobType, success, duration);
    }

    public void RecordTokensConsumed(string model, long tokens, string organizationId)
    {
        _tokenRecords.Enqueue(new TokenRecord
        {
            Timestamp = DateTime.UtcNow,
            Model = model,
            Tokens = tokens,
            OrganizationId = organizationId
        });

        // Trim old records
        while (_tokenRecords.Count > 10000)
            _tokenRecords.TryDequeue(out _);

        var key = $"{MetricNames.TokensConsumed}.{model}";
        _counters.AddOrUpdate(key, tokens, (_, v) => v + tokens);

        _logger.LogDebug(
            "Tokens consumed: {Tokens} ({Model}) for {OrgId}",
            tokens, model, organizationId);
    }

    public void RecordQueueMetrics(int queueLength, int deadLetterLength)
    {
        RecordGauge(MetricNames.QueueLength, queueLength);
        RecordGauge(MetricNames.DeadLetterLength, deadLetterLength);
    }

    public void RecordMemoryUsage(long bytesUsed, long peakBytes)
    {
        RecordGauge(MetricNames.MemoryUsed, bytesUsed);
        RecordGauge(MetricNames.MemoryPeak, peakBytes);
    }

    public void RecordDiskUsage(string path, long freeBytes, long totalBytes)
    {
        var usedPercent = totalBytes > 0 ? (1.0 - (double)freeBytes / totalBytes) * 100 : 0;
        RecordGauge($"{MetricNames.DiskFree}.{path}", freeBytes);
        RecordGauge($"{MetricNames.DiskUsedPercent}.{path}", usedPercent);
    }

    /// <summary>
    /// Gets a snapshot of system health
    /// </summary>
    public SystemHealthSnapshot GetHealthSnapshot()
    {
        var now = DateTime.UtcNow;
        var hourAgo = now.AddHours(-1);

        var recentJobs = _jobRecords
            .Where(j => j.Timestamp >= hourAgo)
            .ToList();

        var recentTokens = _tokenRecords
            .Where(t => t.Timestamp >= hourAgo)
            .Sum(t => t.Tokens);

        var alerts = new List<string>();

        // Check for issues
        var deadLetterCount = (int)(_gauges.GetValueOrDefault(MetricNames.DeadLetterLength, 0));
        if (deadLetterCount > 0)
            alerts.Add($"Dead letter queue has {deadLetterCount} messages");

        var failedJobsLastHour = recentJobs.Count(j => !j.Success);
        if (failedJobsLastHour > 5)
            alerts.Add($"{failedJobsLastHour} jobs failed in the last hour");

        var avgDuration = recentJobs.Count > 0
            ? recentJobs.Average(j => j.Duration.TotalSeconds)
            : 0;

        return new SystemHealthSnapshot
        {
            Timestamp = now,
            QueueLength = (int)_gauges.GetValueOrDefault(MetricNames.QueueLength, 0),
            DeadLetterQueueLength = deadLetterCount,
            ActiveJobs = _activeJobs.Count,
            AvgJobDurationSeconds = avgDuration,
            JobFailuresLastHour = failedJobsLastHour,
            TokensConsumedLastHour = recentTokens,
            PeakMemoryBytes = (long)_gauges.GetValueOrDefault(MetricNames.MemoryPeak, 0),
            DiskFreePercent = 100 - _gauges.GetValueOrDefault(MetricNames.DiskUsedPercent, 0),
            IsHealthy = alerts.Count == 0,
            Alerts = alerts
        };
    }

    private sealed class TimerScope : IDisposable
    {
        private readonly InMemoryMetricsService _service;
        private readonly string _name;
        private readonly IDictionary<string, string>? _tags;
        private readonly Stopwatch _stopwatch;

        public TimerScope(InMemoryMetricsService service, string name, IDictionary<string, string>? tags)
        {
            _service = service;
            _name = name;
            _tags = tags;
            _stopwatch = Stopwatch.StartNew();
        }

        public void Dispose()
        {
            _stopwatch.Stop();
            _service.RecordHistogram(_name, _stopwatch.Elapsed.TotalSeconds, _tags);
        }
    }

    private record JobCompletionRecord
    {
        public Guid ProjectId { get; init; }
        public string JobType { get; init; } = string.Empty;
        public bool Success { get; init; }
        public TimeSpan Duration { get; init; }
        public DateTime CompletedAt { get; init; }
    }

    private record TokenRecord
    {
        public DateTime Timestamp { get; init; }
        public string Model { get; init; } = string.Empty;
        public long Tokens { get; init; }
        public string OrganizationId { get; init; } = string.Empty;
    }

    private record JobRecord
    {
        public DateTime Timestamp { get; init; }
        public TimeSpan Duration { get; init; }
        public bool Success { get; init; }
    }
}
