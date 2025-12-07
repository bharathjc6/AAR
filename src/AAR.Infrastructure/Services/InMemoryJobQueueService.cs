// =============================================================================
// AAR.Infrastructure - Services/InMemoryJobQueueService.cs
// In-memory job queue for development (swap with Azure Service Bus for production)
// =============================================================================

using System.Collections.Concurrent;
using AAR.Application.Interfaces;
using Microsoft.Extensions.Logging;

namespace AAR.Infrastructure.Services;

/// <summary>
/// In-memory implementation of job queue for development/testing
/// For production, use Azure Service Bus or similar durable queue
/// </summary>
public sealed class InMemoryJobQueueService : IJobQueueService
{
    private readonly ConcurrentDictionary<string, QueuedMessage> _messages = new();
    private readonly ConcurrentDictionary<string, QueuedMessage> _deadLetterQueue = new();
    private readonly SortedSet<QueuedMessage> _priorityQueue;
    private readonly object _lock = new();
    private readonly ILogger<InMemoryJobQueueService> _logger;

    public InMemoryJobQueueService(ILogger<InMemoryJobQueueService> logger)
    {
        _logger = logger;
        _priorityQueue = new SortedSet<QueuedMessage>(new MessageComparer());
    }

    public Task<string> EnqueueAsync(
        JobQueueMessage message,
        CancellationToken cancellationToken = default)
    {
        var messageId = Guid.NewGuid().ToString();
        var queuedMessage = new QueuedMessage
        {
            MessageId = messageId,
            Message = message with { EnqueuedAt = DateTime.UtcNow },
            VisibleAt = message.ScheduledFor ?? DateTime.UtcNow,
            DeliveryCount = 0
        };

        lock (_lock)
        {
            _messages[messageId] = queuedMessage;
            _priorityQueue.Add(queuedMessage);
        }

        _logger.LogDebug(
            "Enqueued job {JobId} with priority {Priority}, message ID {MessageId}",
            message.JobId, message.Priority, messageId);

        return Task.FromResult(messageId);
    }

    public Task<string> EnqueueWithDelayAsync(
        JobQueueMessage message,
        TimeSpan delay,
        CancellationToken cancellationToken = default)
    {
        var delayedMessage = message with { ScheduledFor = DateTime.UtcNow.Add(delay) };
        return EnqueueAsync(delayedMessage, cancellationToken);
    }

    public Task<JobQueueMessage?> DequeueAsync(
        TimeSpan? visibilityTimeout = null,
        CancellationToken cancellationToken = default)
    {
        var timeout = visibilityTimeout ?? TimeSpan.FromMinutes(5);
        var now = DateTime.UtcNow;

        lock (_lock)
        {
            var visible = _priorityQueue
                .Where(m => m.VisibleAt <= now && !m.IsLocked)
                .FirstOrDefault();

            if (visible == null)
                return Task.FromResult<JobQueueMessage?>(null);

            // Lock the message
            visible.IsLocked = true;
            visible.LockExpiresAt = now.Add(timeout);
            visible.DeliveryCount++;

            _logger.LogDebug(
                "Dequeued job {JobId}, delivery count {Count}",
                visible.Message.JobId, visible.DeliveryCount);

            return Task.FromResult<JobQueueMessage?>(
                visible.Message with { DeliveryCount = visible.DeliveryCount });
        }
    }

    public Task CompleteAsync(string messageId, CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            if (_messages.TryRemove(messageId, out var message))
            {
                _priorityQueue.Remove(message);
                _logger.LogDebug("Completed message {MessageId}", messageId);
            }
        }
        return Task.CompletedTask;
    }

    public Task AbandonAsync(string messageId, CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            if (_messages.TryGetValue(messageId, out var message))
            {
                message.IsLocked = false;
                message.LockExpiresAt = null;
                message.VisibleAt = DateTime.UtcNow.AddSeconds(30); // 30 second delay before retry
                _logger.LogDebug("Abandoned message {MessageId}, will retry", messageId);
            }
        }
        return Task.CompletedTask;
    }

    public Task DeadLetterAsync(
        string messageId,
        string reason,
        CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            if (_messages.TryRemove(messageId, out var message))
            {
                _priorityQueue.Remove(message);
                _deadLetterQueue[messageId] = message;
                _logger.LogWarning(
                    "Dead-lettered message {MessageId} for job {JobId}: {Reason}",
                    messageId, message.Message.JobId, reason);
            }
        }
        return Task.CompletedTask;
    }

    public Task<int> GetQueueLengthAsync(CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            return Task.FromResult(_priorityQueue.Count(m => !m.IsLocked && m.VisibleAt <= DateTime.UtcNow));
        }
    }

    public Task<int> GetDeadLetterQueueLengthAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(_deadLetterQueue.Count);
    }

    public Task<IReadOnlyList<JobQueueMessage>> PeekAsync(
        int count = 10,
        CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            var messages = _priorityQueue
                .Where(m => m.VisibleAt <= DateTime.UtcNow)
                .Take(count)
                .Select(m => m.Message)
                .ToList();

            return Task.FromResult<IReadOnlyList<JobQueueMessage>>(messages);
        }
    }

    private class QueuedMessage
    {
        public string MessageId { get; init; } = string.Empty;
        public JobQueueMessage Message { get; init; } = null!;
        public DateTime VisibleAt { get; set; }
        public int DeliveryCount { get; set; }
        public bool IsLocked { get; set; }
        public DateTime? LockExpiresAt { get; set; }
    }

    private class MessageComparer : IComparer<QueuedMessage>
    {
        public int Compare(QueuedMessage? x, QueuedMessage? y)
        {
            if (x == null || y == null) return 0;

            // Higher priority first
            var priorityCompare = y.Message.Priority.CompareTo(x.Message.Priority);
            if (priorityCompare != 0) return priorityCompare;

            // Earlier enqueue time first
            var timeCompare = x.Message.EnqueuedAt.CompareTo(y.Message.EnqueuedAt);
            if (timeCompare != 0) return timeCompare;

            // Fall back to message ID for uniqueness
            return string.Compare(x.MessageId, y.MessageId, StringComparison.Ordinal);
        }
    }
}
