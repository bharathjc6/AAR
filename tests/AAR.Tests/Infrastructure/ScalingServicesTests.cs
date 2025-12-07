// =============================================================================
// AAR.Tests - Infrastructure/ScalingServicesTests.cs
// Tests for scaling infrastructure components
// =============================================================================

using AAR.Application.DTOs;
using AAR.Application.Interfaces;
using AAR.Infrastructure.Services;
using AAR.Infrastructure.Services.Telemetry;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;

namespace AAR.Tests.Infrastructure;

/// <summary>
/// Tests for InMemoryJobQueueService
/// </summary>
public class InMemoryJobQueueServiceTests
{
    private readonly Mock<ILogger<InMemoryJobQueueService>> _loggerMock;
    private readonly InMemoryJobQueueService _sut;

    public InMemoryJobQueueServiceTests()
    {
        _loggerMock = new Mock<ILogger<InMemoryJobQueueService>>();
        _sut = new InMemoryJobQueueService(_loggerMock.Object);
    }

    [Fact]
    public async Task EnqueueAsync_ReturnsMessageId()
    {
        // Arrange
        var message = new JobQueueMessage
        {
            JobId = Guid.NewGuid(),
            ProjectId = Guid.NewGuid(),
            Priority = JobPriority.Normal
        };

        // Act
        var messageId = await _sut.EnqueueAsync(message);

        // Assert
        messageId.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task DequeueAsync_ReturnsEnqueuedMessage()
    {
        // Arrange
        var projectId = Guid.NewGuid();
        var message = new JobQueueMessage
        {
            JobId = Guid.NewGuid(),
            ProjectId = projectId,
            Priority = JobPriority.Normal
        };
        await _sut.EnqueueAsync(message);

        // Act
        var dequeued = await _sut.DequeueAsync();

        // Assert
        dequeued.Should().NotBeNull();
        dequeued!.ProjectId.Should().Be(projectId);
    }

    [Fact]
    public async Task DequeueAsync_ReturnsHighestPriorityFirst()
    {
        // Arrange
        var lowPriority = new JobQueueMessage
        {
            JobId = Guid.NewGuid(),
            ProjectId = Guid.NewGuid(),
            Priority = JobPriority.Low
        };
        var highPriority = new JobQueueMessage
        {
            JobId = Guid.NewGuid(),
            ProjectId = Guid.NewGuid(),
            Priority = JobPriority.Critical
        };

        await _sut.EnqueueAsync(lowPriority);
        await _sut.EnqueueAsync(highPriority);

        // Act
        var dequeued = await _sut.DequeueAsync();

        // Assert
        dequeued.Should().NotBeNull();
        dequeued!.Priority.Should().Be(JobPriority.Critical);
    }

    [Fact]
    public async Task DequeueAsync_ReturnsNull_WhenEmpty()
    {
        // Act
        var result = await _sut.DequeueAsync();

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task GetQueueLengthAsync_ReturnsCorrectCount()
    {
        // Arrange
        await _sut.EnqueueAsync(new JobQueueMessage { JobId = Guid.NewGuid(), ProjectId = Guid.NewGuid() });
        await _sut.EnqueueAsync(new JobQueueMessage { JobId = Guid.NewGuid(), ProjectId = Guid.NewGuid() });

        // Act
        var length = await _sut.GetQueueLengthAsync();

        // Assert
        length.Should().Be(2);
    }

    [Fact]
    public async Task CompleteAsync_RemovesFromQueue()
    {
        // Arrange
        var message = new JobQueueMessage
        {
            JobId = Guid.NewGuid(),
            ProjectId = Guid.NewGuid()
        };
        var messageId = await _sut.EnqueueAsync(message);
        await _sut.DequeueAsync(); // Dequeue to lock

        // Act
        await _sut.CompleteAsync(messageId);
        var length = await _sut.GetQueueLengthAsync();

        // Assert
        length.Should().Be(0);
    }

    [Fact]
    public async Task DeadLetterAsync_MovesToDeadLetterQueue()
    {
        // Arrange
        var message = new JobQueueMessage
        {
            JobId = Guid.NewGuid(),
            ProjectId = Guid.NewGuid()
        };
        var messageId = await _sut.EnqueueAsync(message);
        await _sut.DequeueAsync();

        // Act
        await _sut.DeadLetterAsync(messageId, "Test reason");
        var dlqLength = await _sut.GetDeadLetterQueueLengthAsync();

        // Assert
        dlqLength.Should().Be(1);
    }
}

/// <summary>
/// Tests for InMemoryMetricsService
/// </summary>
public class InMemoryMetricsServiceTests
{
    private readonly Mock<ILogger<InMemoryMetricsService>> _loggerMock;
    private readonly InMemoryMetricsService _sut;

    public InMemoryMetricsServiceTests()
    {
        _loggerMock = new Mock<ILogger<InMemoryMetricsService>>();
        _sut = new InMemoryMetricsService(_loggerMock.Object);
    }

    [Fact]
    public void IncrementCounter_IncrementsValue()
    {
        // Arrange
        var name = "test_counter";

        // Act
        _sut.IncrementCounter(name);
        _sut.IncrementCounter(name);
        _sut.IncrementCounter(name);

        // Assert - verify no exceptions thrown, logging occurred
        _loggerMock.Verify(l => l.Log(
            It.IsAny<LogLevel>(),
            It.IsAny<EventId>(),
            It.IsAny<It.IsAnyType>(),
            It.IsAny<Exception?>(),
            It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.AtLeast(3));
    }

    [Fact]
    public void RecordGauge_RecordsValue()
    {
        // Arrange
        var name = "test_gauge";
        var value = 42.0;

        // Act & Assert - should not throw
        var act = () => _sut.RecordGauge(name, value);
        act.Should().NotThrow();
    }

    [Fact]
    public void RecordHistogram_RecordsValue()
    {
        // Arrange
        var name = "test_histogram";

        // Act & Assert - should not throw
        var act = () => _sut.RecordHistogram(name, 100);
        act.Should().NotThrow();
    }

    [Fact]
    public void StartTimer_ReturnsDisposable()
    {
        // Arrange
        var name = "test_timer";

        // Act
        using var timer = _sut.StartTimer(name);

        // Assert
        timer.Should().NotBeNull();
    }

    [Fact]
    public void RecordJobStart_RecordsJob()
    {
        // Arrange
        var projectId = Guid.NewGuid();
        var jobType = "Analysis";

        // Act & Assert
        var act = () => _sut.RecordJobStart(projectId, jobType);
        act.Should().NotThrow();
    }

    [Fact]
    public void RecordJobCompletion_RecordsCompletion()
    {
        // Arrange
        var projectId = Guid.NewGuid();
        _sut.RecordJobStart(projectId, "Analysis");

        // Act & Assert
        var act = () => _sut.RecordJobCompletion(projectId, "Analysis", true, TimeSpan.FromSeconds(30));
        act.Should().NotThrow();
    }

    [Fact]
    public void RecordTokensConsumed_RecordsTokens()
    {
        // Arrange
        var model = "text-embedding-3-small";
        var tokens = 10000L;
        var orgId = "org-123";

        // Act & Assert
        var act = () => _sut.RecordTokensConsumed(model, tokens, orgId);
        act.Should().NotThrow();
    }
}

/// <summary>
/// Tests for JobProgressService
/// </summary>
public class JobProgressServiceTests
{
    private readonly Mock<IAnalysisHubNotifier> _hubNotifierMock;
    private readonly Mock<IMetricsService> _metricsMock;
    private readonly Mock<ILogger<JobProgressService>> _loggerMock;
    private readonly JobProgressService _sut;

    public JobProgressServiceTests()
    {
        _hubNotifierMock = new Mock<IAnalysisHubNotifier>();
        _metricsMock = new Mock<IMetricsService>();
        _loggerMock = new Mock<ILogger<JobProgressService>>();
        _sut = new JobProgressService(_hubNotifierMock.Object, _metricsMock.Object, _loggerMock.Object);
    }

    [Fact]
    public async Task ReportProgressAsync_SendsNotification()
    {
        // Arrange
        var progress = new JobProgressUpdate
        {
            ProjectId = Guid.NewGuid(),
            ProgressPercent = 50,
            Phase = "Analyzing",
            Message = "Processing files..."
        };

        // Act
        await _sut.ReportProgressAsync(progress);

        // Assert
        _hubNotifierMock.Verify(
            h => h.SendProgressAsync(progress.ProjectId, It.IsAny<JobProgressUpdate>()),
            Times.Once);
    }

    [Fact]
    public async Task ReportFindingAsync_SendsFindingNotification()
    {
        // Arrange
        var finding = new PartialFindingUpdate
        {
            ProjectId = Guid.NewGuid(),
            Finding = new FindingSummary
            {
                Id = Guid.NewGuid(),
                Severity = "Warning",
                Category = "CodeQuality",
                Description = "Test finding"
            },
            Timestamp = DateTime.UtcNow
        };

        // Act
        await _sut.ReportFindingAsync(finding);

        // Assert
        _hubNotifierMock.Verify(
            h => h.SendFindingAsync(finding.ProjectId, It.IsAny<PartialFindingUpdate>()),
            Times.Once);
    }

    [Fact]
    public async Task ReportCompletionAsync_SendsCompletionNotification()
    {
        // Arrange
        var completion = new JobCompletionUpdate
        {
            ProjectId = Guid.NewGuid(),
            IsSuccess = true,
            ReportId = Guid.NewGuid()
        };

        // Act
        await _sut.ReportCompletionAsync(completion);

        // Assert
        _hubNotifierMock.Verify(
            h => h.SendCompletionAsync(completion.ProjectId, It.IsAny<JobCompletionUpdate>()),
            Times.Once);
    }
}
