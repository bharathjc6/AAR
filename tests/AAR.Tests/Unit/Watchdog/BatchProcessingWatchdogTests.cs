// =============================================================================
// AAR.Tests - Unit/Watchdog/BatchProcessingWatchdogTests.cs
// Unit tests for the BatchProcessingWatchdog service
// =============================================================================

using AAR.Application.Interfaces;
using AAR.Infrastructure.Services.Watchdog;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace AAR.Tests.Unit.Watchdog;

/// <summary>
/// Unit tests for BatchProcessingWatchdog to ensure stuck detection and recovery works correctly
/// </summary>
public class BatchProcessingWatchdogTests
{
    private readonly Mock<ILogger<BatchProcessingWatchdog>> _loggerMock;
    private readonly Mock<IMetricsService> _metricsMock;

    public BatchProcessingWatchdogTests()
    {
        _loggerMock = new Mock<ILogger<BatchProcessingWatchdog>>();
        _metricsMock = new Mock<IMetricsService>();
    }

    [Fact]
    public void TrackBatch_CreatesLinkedCancellationToken()
    {
        // Arrange
        var options = Options.Create(new WatchdogOptions { Enabled = true });
        var watchdog = new BatchProcessingWatchdog(options, _loggerMock.Object, _metricsMock.Object);
        var projectId = Guid.NewGuid();
        var externalCts = new CancellationTokenSource();

        // Act
        using var linkedCts = watchdog.TrackBatch(projectId, 0, 10, externalCts.Token);

        // Assert
        linkedCts.Should().NotBeNull();
        linkedCts.Token.IsCancellationRequested.Should().BeFalse();

        // When external is cancelled, linked should be cancelled too
        externalCts.Cancel();
        linkedCts.Token.IsCancellationRequested.Should().BeTrue();
    }

    [Fact]
    public void TrackBatch_AddsProjectToTracking()
    {
        // Arrange
        var options = Options.Create(new WatchdogOptions { Enabled = true });
        var watchdog = new BatchProcessingWatchdog(options, _loggerMock.Object, _metricsMock.Object);
        var projectId = Guid.NewGuid();

        // Act
        using var _ = watchdog.TrackBatch(projectId, 0, 10, CancellationToken.None);
        var trackedProjects = watchdog.GetTrackedProjects();

        // Assert
        trackedProjects.Should().HaveCount(1);
        trackedProjects[0].ProjectId.Should().Be(projectId);
        trackedProjects[0].TotalBatches.Should().Be(10);
    }

    [Fact]
    public void UpdatePhase_UpdatesPhaseAndHeartbeat()
    {
        // Arrange
        var options = Options.Create(new WatchdogOptions { Enabled = true });
        var watchdog = new BatchProcessingWatchdog(options, _loggerMock.Object, _metricsMock.Object);
        var projectId = Guid.NewGuid();

        using var _ = watchdog.TrackBatch(projectId, 0, 10, CancellationToken.None);
        var initialHeartbeat = watchdog.GetTrackedProjects()[0].LastHeartbeat;

        // Add a small delay to ensure time difference
        Thread.Sleep(10);

        // Act
        watchdog.UpdatePhase(projectId, "Processing batch 5/10");

        // Assert
        var project = watchdog.GetTrackedProjects()[0];
        project.CurrentPhase.Should().Be("Processing batch 5/10");
        project.LastHeartbeat.Should().BeAfter(initialHeartbeat);
    }

    [Fact]
    public void Heartbeat_UpdatesLastHeartbeatAndResetsStuckCount()
    {
        // Arrange
        var options = Options.Create(new WatchdogOptions { Enabled = true });
        var watchdog = new BatchProcessingWatchdog(options, _loggerMock.Object, _metricsMock.Object);
        var projectId = Guid.NewGuid();

        using var _ = watchdog.TrackBatch(projectId, 0, 10, CancellationToken.None);
        var initialHeartbeat = watchdog.GetTrackedProjects()[0].LastHeartbeat;

        // Simulate time passing
        Thread.Sleep(10);

        // Act
        watchdog.Heartbeat(projectId);

        // Assert
        var project = watchdog.GetTrackedProjects()[0];
        project.LastHeartbeat.Should().BeAfter(initialHeartbeat);
        project.StuckDetectionCount.Should().Be(0);
    }

    [Fact]
    public void Complete_RemovesProjectFromTracking()
    {
        // Arrange
        var options = Options.Create(new WatchdogOptions { Enabled = true });
        var watchdog = new BatchProcessingWatchdog(options, _loggerMock.Object, _metricsMock.Object);
        var projectId = Guid.NewGuid();

        using var _ = watchdog.TrackBatch(projectId, 0, 10, CancellationToken.None);
        watchdog.GetTrackedProjects().Should().HaveCount(1);

        // Act
        watchdog.Complete(projectId);

        // Assert
        watchdog.GetTrackedProjects().Should().BeEmpty();
    }

    [Fact]
    public void IsProjectStuck_ReturnsFalse_WhenProjectNotTracked()
    {
        // Arrange
        var options = Options.Create(new WatchdogOptions { Enabled = true });
        var watchdog = new BatchProcessingWatchdog(options, _loggerMock.Object, _metricsMock.Object);

        // Act
        var isStuck = watchdog.IsProjectStuck(Guid.NewGuid());

        // Assert
        isStuck.Should().BeFalse();
    }

    [Fact]
    public void IsProjectStuck_ReturnsFalse_WhenWithinTimeouts()
    {
        // Arrange
        var options = Options.Create(new WatchdogOptions
        {
            Enabled = true,
            MaxProjectDurationSeconds = 600,
            MaxHeartbeatIntervalSeconds = 120
        });
        var watchdog = new BatchProcessingWatchdog(options, _loggerMock.Object, _metricsMock.Object);
        var projectId = Guid.NewGuid();

        using var _ = watchdog.TrackBatch(projectId, 0, 10, CancellationToken.None);
        watchdog.Heartbeat(projectId);

        // Act
        var isStuck = watchdog.IsProjectStuck(projectId);

        // Assert
        isStuck.Should().BeFalse();
    }

    [Fact]
    public void IsProjectStuck_ReturnsTrue_WhenExceedsProjectDuration()
    {
        // Arrange - very short timeout for testing
        var options = Options.Create(new WatchdogOptions
        {
            Enabled = true,
            MaxProjectDurationSeconds = 0, // Immediate timeout
            MaxHeartbeatIntervalSeconds = 120
        });
        var watchdog = new BatchProcessingWatchdog(options, _loggerMock.Object, _metricsMock.Object);
        var projectId = Guid.NewGuid();

        using var _ = watchdog.TrackBatch(projectId, 0, 10, CancellationToken.None);

        // Small delay to ensure we exceed 0 seconds
        Thread.Sleep(10);

        // Act
        var isStuck = watchdog.IsProjectStuck(projectId);

        // Assert
        isStuck.Should().BeTrue();
    }

    [Fact]
    public void IsProjectStuck_ReturnsTrue_WhenNoHeartbeatReceived()
    {
        // Arrange - very short heartbeat interval for testing
        var options = Options.Create(new WatchdogOptions
        {
            Enabled = true,
            MaxProjectDurationSeconds = 600,
            MaxHeartbeatIntervalSeconds = 0 // Immediate heartbeat timeout
        });
        var watchdog = new BatchProcessingWatchdog(options, _loggerMock.Object, _metricsMock.Object);
        var projectId = Guid.NewGuid();

        using var _ = watchdog.TrackBatch(projectId, 0, 10, CancellationToken.None);

        // Small delay to ensure we exceed 0 seconds
        Thread.Sleep(10);

        // Act
        var isStuck = watchdog.IsProjectStuck(projectId);

        // Assert
        isStuck.Should().BeTrue();
    }

    [Fact]
    public void TrackBatch_MultipleProjects_TrackedIndependently()
    {
        // Arrange
        var options = Options.Create(new WatchdogOptions { Enabled = true });
        var watchdog = new BatchProcessingWatchdog(options, _loggerMock.Object, _metricsMock.Object);
        var projectId1 = Guid.NewGuid();
        var projectId2 = Guid.NewGuid();

        // Act
        using var cts1 = watchdog.TrackBatch(projectId1, 0, 10, CancellationToken.None);
        using var cts2 = watchdog.TrackBatch(projectId2, 0, 20, CancellationToken.None);

        watchdog.UpdatePhase(projectId1, "Phase 1");
        watchdog.UpdatePhase(projectId2, "Phase 2");

        // Assert
        var projects = watchdog.GetTrackedProjects();
        projects.Should().HaveCount(2);
        projects.Should().Contain(p => p.ProjectId == projectId1 && p.CurrentPhase == "Phase 1");
        projects.Should().Contain(p => p.ProjectId == projectId2 && p.CurrentPhase == "Phase 2");
    }
}
