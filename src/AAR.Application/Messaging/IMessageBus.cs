// =============================================================================
// AAR.Application - Messaging/IMessageBus.cs
// Abstraction for message bus operations using MassTransit
// =============================================================================

namespace AAR.Application.Messaging;

/// <summary>
/// Simplified message bus abstraction over MassTransit
/// </summary>
public interface IMessageBus
{
    /// <summary>
    /// Publishes an event to all subscribers
    /// </summary>
    /// <typeparam name="T">Event type</typeparam>
    /// <param name="message">The event to publish</param>
    /// <param name="cancellationToken">Cancellation token</param>
    Task PublishAsync<T>(T message, CancellationToken cancellationToken = default) where T : class;

    /// <summary>
    /// Sends a command to a specific queue/endpoint
    /// </summary>
    /// <typeparam name="T">Command type</typeparam>
    /// <param name="message">The command to send</param>
    /// <param name="cancellationToken">Cancellation token</param>
    Task SendAsync<T>(T message, CancellationToken cancellationToken = default) where T : class;
}
