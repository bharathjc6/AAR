// =============================================================================
// AAR.Infrastructure - Messaging/MassTransitMessageBus.cs
// MassTransit implementation of IMessageBus
// =============================================================================

using AAR.Application.Messaging;
using MassTransit;
using Microsoft.Extensions.Logging;

namespace AAR.Infrastructure.Messaging;

/// <summary>
/// MassTransit implementation of the message bus
/// </summary>
public class MassTransitMessageBus : IMessageBus
{
    private readonly IPublishEndpoint _publishEndpoint;
    private readonly ISendEndpointProvider _sendEndpointProvider;
    private readonly ILogger<MassTransitMessageBus> _logger;

    public MassTransitMessageBus(
        IPublishEndpoint publishEndpoint,
        ISendEndpointProvider sendEndpointProvider,
        ILogger<MassTransitMessageBus> logger)
    {
        _publishEndpoint = publishEndpoint;
        _sendEndpointProvider = sendEndpointProvider;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task PublishAsync<T>(T message, CancellationToken cancellationToken = default) where T : class
    {
        _logger.LogDebug("Publishing event of type {EventType}", typeof(T).Name);
        
        await _publishEndpoint.Publish(message, cancellationToken);
        
        _logger.LogInformation("Published event: {EventType}", typeof(T).Name);
    }

    /// <inheritdoc/>
    public async Task SendAsync<T>(T message, CancellationToken cancellationToken = default) where T : class
    {
        _logger.LogDebug("Sending command of type {CommandType}", typeof(T).Name);
        
        // Use the same queue name that the consumer is configured on
        // The consumer is registered on "analysis-jobs" queue
        const string queueName = "analysis-jobs";
        var endpoint = await _sendEndpointProvider.GetSendEndpoint(
            new Uri($"queue:{queueName}"));
        
        await endpoint.Send(message, cancellationToken);
        
        _logger.LogInformation("Sent command: {CommandType} to queue: {QueueName}", typeof(T).Name, queueName);
    }
}
