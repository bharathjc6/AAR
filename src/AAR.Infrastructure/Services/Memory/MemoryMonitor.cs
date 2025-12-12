// =============================================================================
// AAR.Infrastructure - Services/Memory/MemoryMonitor.cs
// Memory monitoring and management service
// =============================================================================

using AAR.Application.Configuration;
using AAR.Application.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Diagnostics;

namespace AAR.Infrastructure.Services.Memory;

/// <summary>
/// Monitors memory usage and provides safeguards for processing.
/// </summary>
public class MemoryMonitor : IMemoryMonitor
{
    private readonly MemoryManagementOptions _options;
    private readonly IMetricsService _metrics;
    private readonly ILogger<MemoryMonitor> _logger;
    private readonly long _maxMemoryBytes;
    private DateTime _lastSampleTime = DateTime.MinValue;

    public MemoryMonitor(
        IOptions<MemoryManagementOptions> options,
        IMetricsService metrics,
        ILogger<MemoryMonitor> logger)
    {
        _options = options.Value;
        _metrics = metrics;
        _logger = logger;
        _maxMemoryBytes = _options.MaxWorkerMemoryMB * 1024L * 1024L;
    }

    /// <inheritdoc/>
    public long CurrentMemoryMB
    {
        get
        {
            var process = Process.GetCurrentProcess();
            return process.WorkingSet64 / 1024 / 1024;
        }
    }

    /// <inheritdoc/>
    public int MemoryUsagePercent
    {
        get
        {
            var currentBytes = Process.GetCurrentProcess().WorkingSet64;
            return (int)(currentBytes * 100 / _maxMemoryBytes);
        }
    }

    /// <inheritdoc/>
    public bool IsMemoryWarning => MemoryUsagePercent >= _options.MemoryWarningThresholdPercent;

    /// <inheritdoc/>
    public bool ShouldPauseProcessing => MemoryUsagePercent >= _options.MemoryPauseThresholdPercent;

    /// <inheritdoc/>
    public void RequestGCIfNeeded()
    {
        if (IsMemoryWarning)
        {
            _logger.LogInformation(
                "Memory at {Percent}% ({MemoryMB} MB), requesting GC",
                MemoryUsagePercent, CurrentMemoryMB);

            GC.Collect(1, GCCollectionMode.Optimized, blocking: false);
        }
    }

    /// <inheritdoc/>
    public void ForceAggressiveGC()
    {
        var beforeMB = CurrentMemoryMB;
        
        GC.Collect(2, GCCollectionMode.Aggressive, blocking: true, compacting: true);
        GC.WaitForPendingFinalizers();
        GC.Collect(2, GCCollectionMode.Aggressive, blocking: true, compacting: true);

        var afterMB = CurrentMemoryMB;
        
        _logger.LogInformation(
            "Aggressive GC completed: {BeforeMB} MB -> {AfterMB} MB (freed {FreedMB} MB)",
            beforeMB, afterMB, beforeMB - afterMB);
    }

    /// <inheritdoc/>
    public void RecordMemorySample()
    {
        var now = DateTime.UtcNow;
        if ((now - _lastSampleTime).TotalSeconds < _options.MemoryCheckIntervalSeconds)
        {
            return;
        }

        _lastSampleTime = now;
        var memoryMB = CurrentMemoryMB;

        // Record metric
        _metrics.RecordGauge("worker_memory_mb", memoryMB);
        _metrics.RecordGauge("worker_memory_percent", MemoryUsagePercent);

        if (ShouldPauseProcessing)
        {
            _logger.LogError(
                "MEMORY CRITICAL: {Percent}% ({MemoryMB} MB) - processing should pause",
                MemoryUsagePercent, memoryMB);
            _metrics.IncrementCounter("worker_memory_pause_triggered");
        }
        else if (IsMemoryWarning)
        {
            _logger.LogWarning(
                "Memory warning: {Percent}% ({MemoryMB} MB)",
                MemoryUsagePercent, memoryMB);
            _metrics.IncrementCounter("worker_memory_warnings");
        }
    }
}
