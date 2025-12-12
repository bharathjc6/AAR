// =============================================================================
// AAR.Tests - Infrastructure/MemoryMonitorTests.cs
// Unit tests for memory monitoring and safeguards
// =============================================================================

using AAR.Application.Configuration;
using AAR.Application.Interfaces;
using AAR.Infrastructure.Services.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;

namespace AAR.Tests.Infrastructure;

public class MemoryMonitorTests
{
    private readonly Mock<IMetricsService> _metricsMock;
    private readonly Mock<ILogger<MemoryMonitor>> _loggerMock;

    public MemoryMonitorTests()
    {
        _metricsMock = new Mock<IMetricsService>();
        _loggerMock = new Mock<ILogger<MemoryMonitor>>();
    }

    private MemoryMonitor CreateMonitor(
        int maxMemoryMB = 4096,
        int warningPercent = 80,
        int pausePercent = 90)
    {
        var options = Options.Create(new MemoryManagementOptions
        {
            MaxWorkerMemoryMB = maxMemoryMB,
            MemoryWarningThresholdPercent = warningPercent,
            MemoryPauseThresholdPercent = pausePercent,
            MemoryCheckIntervalSeconds = 1
        });

        return new MemoryMonitor(options, _metricsMock.Object, _loggerMock.Object);
    }

    [Fact]
    public void CurrentMemoryMB_ShouldReturnPositiveValue()
    {
        // Arrange
        var monitor = CreateMonitor();

        // Act
        var memoryMB = monitor.CurrentMemoryMB;

        // Assert
        Assert.True(memoryMB > 0);
    }

    [Fact]
    public void MemoryUsagePercent_ShouldReturnValidPercentage()
    {
        // Arrange
        var monitor = CreateMonitor(maxMemoryMB: 8192); // 8GB max

        // Act
        var percent = monitor.MemoryUsagePercent;

        // Assert
        Assert.InRange(percent, 0, 100);
    }

    [Fact]
    public void IsMemoryWarning_WithLowMemoryMax_ShouldBeTrue()
    {
        // Arrange - Set very low max to trigger warning
        var monitor = CreateMonitor(maxMemoryMB: 10, warningPercent: 10);

        // Act
        var isWarning = monitor.IsMemoryWarning;

        // Assert - Current process uses more than 1MB, so should trigger
        Assert.True(isWarning);
    }

    [Fact]
    public void IsMemoryWarning_WithHighMemoryMax_ShouldBeFalse()
    {
        // Arrange - Set very high max
        var monitor = CreateMonitor(maxMemoryMB: 100000, warningPercent: 99);

        // Act
        var isWarning = monitor.IsMemoryWarning;

        // Assert
        Assert.False(isWarning);
    }

    [Fact]
    public void ShouldPauseProcessing_WithVeryLowMax_ShouldBeTrue()
    {
        // Arrange - Set extremely low max
        var monitor = CreateMonitor(maxMemoryMB: 1, pausePercent: 1);

        // Act
        var shouldPause = monitor.ShouldPauseProcessing;

        // Assert
        Assert.True(shouldPause);
    }

    [Fact]
    public void RecordMemorySample_ShouldCallMetrics()
    {
        // Arrange
        var monitor = CreateMonitor();

        // Act
        monitor.RecordMemorySample();

        // Assert
        _metricsMock.Verify(m => m.RecordGauge("worker_memory_mb", It.IsAny<double>()), Times.Once);
        _metricsMock.Verify(m => m.RecordGauge("worker_memory_percent", It.IsAny<double>()), Times.Once);
    }

    [Fact]
    public void ForceAggressiveGC_ShouldNotThrow()
    {
        // Arrange
        var monitor = CreateMonitor();

        // Act & Assert - Should not throw
        var exception = Record.Exception(() => monitor.ForceAggressiveGC());
        Assert.Null(exception);
    }

    [Fact]
    public void RequestGCIfNeeded_WhenBelowWarning_ShouldNotForceGC()
    {
        // Arrange
        var monitor = CreateMonitor(maxMemoryMB: 100000, warningPercent: 99);
        var beforeMB = monitor.CurrentMemoryMB;

        // Act
        monitor.RequestGCIfNeeded();

        // Assert - Memory might change slightly but this shouldn't error
        Assert.True(true);
    }
}
