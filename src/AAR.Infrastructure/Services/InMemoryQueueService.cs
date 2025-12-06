// =============================================================================
// AAR.Infrastructure - Services/InMemoryQueueService.cs
// In-memory queue implementation for local development
// =============================================================================

using AAR.Application.Interfaces;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Text.Json;

namespace AAR.Infrastructure.Services;

/// <summary>
/// In-memory queue implementation for local development and testing
/// </summary>
public class InMemoryQueueService : IQueueService
{
    private readonly ConcurrentDictionary<string, ConcurrentQueue<QueueItem>> _queues = new();
    private readonly ILogger<InMemoryQueueService> _logger;

    public InMemoryQueueService(ILogger<InMemoryQueueService> logger)
    {
        _logger = logger;
        _logger.LogInformation("InMemoryQueueService initialized");
    }

    private ConcurrentQueue<QueueItem> GetOrCreateQueue(string queueName)
    {
        return _queues.GetOrAdd(queueName, _ => new ConcurrentQueue<QueueItem>());
    }

    /// <inheritdoc/>
    public Task EnqueueAsync<T>(
        string queueName, 
        T message, 
        TimeSpan? visibilityTimeout = null,
        CancellationToken cancellationToken = default) where T : class
    {
        var queue = GetOrCreateQueue(queueName);
        var item = new QueueItem
        {
            MessageId = Guid.NewGuid().ToString(),
            PopReceipt = Guid.NewGuid().ToString(),
            Content = JsonSerializer.Serialize(message),
            TypeName = typeof(T).FullName ?? typeof(T).Name,
            InsertedOn = DateTime.UtcNow,
            VisibleAt = DateTime.UtcNow + (visibilityTimeout ?? TimeSpan.Zero)
        };
        
        queue.Enqueue(item);
        
        _logger.LogDebug("Enqueued message to {QueueName}: {MessageId}", queueName, item.MessageId);
        
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task<QueueMessage<T>?> DequeueAsync<T>(
        string queueName, 
        CancellationToken cancellationToken = default) where T : class
    {
        var queue = GetOrCreateQueue(queueName);
        
        // Try to find a visible message
        while (queue.TryDequeue(out var item))
        {
            if (item.VisibleAt > DateTime.UtcNow)
            {
                // Not visible yet, re-queue
                queue.Enqueue(item);
                continue;
            }

            item.DequeueCount++;
            
            var content = JsonSerializer.Deserialize<T>(item.Content);
            if (content is null)
            {
                _logger.LogWarning("Failed to deserialize message {MessageId} from {QueueName}", 
                    item.MessageId, queueName);
                continue;
            }

            _logger.LogDebug("Dequeued message from {QueueName}: {MessageId}", queueName, item.MessageId);
            
            return Task.FromResult<QueueMessage<T>?>(new QueueMessage<T>
            {
                Content = content,
                MessageId = item.MessageId,
                PopReceipt = item.PopReceipt,
                DequeueCount = item.DequeueCount,
                InsertedOn = item.InsertedOn
            });
        }

        return Task.FromResult<QueueMessage<T>?>(null);
    }

    /// <inheritdoc/>
    public Task<T?> PeekAsync<T>(
        string queueName, 
        CancellationToken cancellationToken = default) where T : class
    {
        var queue = GetOrCreateQueue(queueName);
        
        if (queue.TryPeek(out var item) && item.VisibleAt <= DateTime.UtcNow)
        {
            var content = JsonSerializer.Deserialize<T>(item.Content);
            return Task.FromResult(content);
        }

        return Task.FromResult<T?>(null);
    }

    /// <inheritdoc/>
    public Task DeleteMessageAsync(
        string queueName, 
        string messageId, 
        string popReceipt,
        CancellationToken cancellationToken = default)
    {
        // In the in-memory implementation, messages are already removed on dequeue
        // This is a no-op
        _logger.LogDebug("Delete message called for {MessageId} in {QueueName} (no-op in memory)", 
            messageId, queueName);
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task<int> GetQueueLengthAsync(
        string queueName, 
        CancellationToken cancellationToken = default)
    {
        var queue = GetOrCreateQueue(queueName);
        return Task.FromResult(queue.Count);
    }

    /// <inheritdoc/>
    public Task ClearQueueAsync(
        string queueName, 
        CancellationToken cancellationToken = default)
    {
        if (_queues.TryGetValue(queueName, out var queue))
        {
            while (queue.TryDequeue(out _)) { }
        }
        
        _logger.LogDebug("Cleared queue: {QueueName}", queueName);
        return Task.CompletedTask;
    }

    private class QueueItem
    {
        public required string MessageId { get; init; }
        public required string PopReceipt { get; init; }
        public required string Content { get; init; }
        public required string TypeName { get; init; }
        public DateTime InsertedOn { get; init; }
        public DateTime VisibleAt { get; set; }
        public int DequeueCount { get; set; }
    }
}
