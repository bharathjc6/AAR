// =============================================================================
// AAR.Application - Interfaces/IQueueService.cs
// Abstraction for message queue operations
// =============================================================================

namespace AAR.Application.Interfaces;

/// <summary>
/// Interface for message queue operations
/// Implementations can target in-memory queue or Azure Queue Storage
/// </summary>
public interface IQueueService
{
    /// <summary>
    /// Enqueues a message
    /// </summary>
    /// <typeparam name="T">Message type</typeparam>
    /// <param name="queueName">Name of the queue</param>
    /// <param name="message">Message to enqueue</param>
    /// <param name="visibilityTimeout">Optional delay before message becomes visible</param>
    /// <param name="cancellationToken">Cancellation token</param>
    Task EnqueueAsync<T>(
        string queueName, 
        T message, 
        TimeSpan? visibilityTimeout = null,
        CancellationToken cancellationToken = default) where T : class;

    /// <summary>
    /// Dequeues a message
    /// </summary>
    /// <typeparam name="T">Message type</typeparam>
    /// <param name="queueName">Name of the queue</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The message or null if queue is empty</returns>
    Task<QueueMessage<T>?> DequeueAsync<T>(
        string queueName, 
        CancellationToken cancellationToken = default) where T : class;

    /// <summary>
    /// Peeks at the next message without removing it
    /// </summary>
    Task<T?> PeekAsync<T>(
        string queueName, 
        CancellationToken cancellationToken = default) where T : class;

    /// <summary>
    /// Deletes a message after processing
    /// </summary>
    Task DeleteMessageAsync(
        string queueName, 
        string messageId, 
        string popReceipt,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the approximate number of messages in the queue
    /// </summary>
    Task<int> GetQueueLengthAsync(
        string queueName, 
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Clears all messages from a queue
    /// </summary>
    Task ClearQueueAsync(
        string queueName, 
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Wrapper for queue messages with metadata
/// </summary>
/// <typeparam name="T">Message type</typeparam>
public record QueueMessage<T> where T : class
{
    /// <summary>
    /// The message content
    /// </summary>
    public required T Content { get; init; }
    
    /// <summary>
    /// Message ID for deletion
    /// </summary>
    public required string MessageId { get; init; }
    
    /// <summary>
    /// Pop receipt for deletion (Azure Queue specific)
    /// </summary>
    public required string PopReceipt { get; init; }
    
    /// <summary>
    /// Number of times this message has been dequeued
    /// </summary>
    public int DequeueCount { get; init; }
    
    /// <summary>
    /// When the message was inserted
    /// </summary>
    public DateTime? InsertedOn { get; init; }
}
