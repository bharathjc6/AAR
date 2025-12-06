// =============================================================================
// AAR.Infrastructure - Services/AzureQueueService.cs
// Azure Queue Storage implementation (stub with TODO for real credentials)
// =============================================================================

using AAR.Application.Interfaces;
using Azure.Storage.Queues;
using Azure.Storage.Queues.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Text;
using System.Text.Json;

namespace AAR.Infrastructure.Services;

/// <summary>
/// Azure Queue Storage implementation
/// TODO: Configure AZURE_STORAGE_CONNECTION_STRING environment variable
/// </summary>
public class AzureQueueService : IQueueService
{
    private readonly AzureQueueStorageOptions _options;
    private readonly QueueServiceClient? _queueServiceClient;
    private readonly ILogger<AzureQueueService> _logger;
    private readonly bool _isConfigured;

    public AzureQueueService(
        IOptions<AzureQueueStorageOptions> options,
        ILogger<AzureQueueService> logger)
    {
        _options = options.Value;
        _logger = logger;

        // TODO: Set this environment variable with your Azure Storage connection string
        var connectionString = _options.ConnectionString 
            ?? Environment.GetEnvironmentVariable("AZURE_STORAGE_CONNECTION_STRING");

        if (!string.IsNullOrEmpty(connectionString) && !connectionString.Contains("TODO"))
        {
            try
            {
                _queueServiceClient = new QueueServiceClient(connectionString);
                _isConfigured = true;
                _logger.LogInformation("AzureQueueService initialized successfully");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to initialize Azure Queue Storage client. Using mock mode.");
                _isConfigured = false;
            }
        }
        else
        {
            _logger.LogWarning("Azure Storage connection string not configured. Queue operations will fail.");
            _isConfigured = false;
        }
    }

    private void EnsureConfigured()
    {
        if (!_isConfigured || _queueServiceClient is null)
        {
            throw new InvalidOperationException(
                "Azure Queue Storage is not configured. " +
                "Set AZURE_STORAGE_CONNECTION_STRING environment variable or use InMemoryQueueService for local development.");
        }
    }

    private async Task<QueueClient> GetQueueClientAsync(string queueName, CancellationToken cancellationToken)
    {
        EnsureConfigured();
        var queueClient = _queueServiceClient!.GetQueueClient(queueName);
        await queueClient.CreateIfNotExistsAsync(cancellationToken: cancellationToken);
        return queueClient;
    }

    /// <inheritdoc/>
    public async Task EnqueueAsync<T>(
        string queueName, 
        T message, 
        TimeSpan? visibilityTimeout = null,
        CancellationToken cancellationToken = default) where T : class
    {
        var queueClient = await GetQueueClientAsync(queueName, cancellationToken);
        
        var json = JsonSerializer.Serialize(message);
        var base64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(json));
        
        await queueClient.SendMessageAsync(
            base64, 
            visibilityTimeout: visibilityTimeout,
            cancellationToken: cancellationToken);

        _logger.LogDebug("Enqueued message to Azure Queue: {QueueName}", queueName);
    }

    /// <inheritdoc/>
    public async Task<QueueMessage<T>?> DequeueAsync<T>(
        string queueName, 
        CancellationToken cancellationToken = default) where T : class
    {
        var queueClient = await GetQueueClientAsync(queueName, cancellationToken);
        
        var response = await queueClient.ReceiveMessageAsync(
            visibilityTimeout: TimeSpan.FromMinutes(5),
            cancellationToken: cancellationToken);

        if (response.Value is null)
        {
            return null;
        }

        var azureMessage = response.Value;
        
        try
        {
            var json = Encoding.UTF8.GetString(Convert.FromBase64String(azureMessage.MessageText));
            var content = JsonSerializer.Deserialize<T>(json);
            
            if (content is null)
            {
                _logger.LogWarning("Failed to deserialize message from Azure Queue: {QueueName}", queueName);
                return null;
            }

            _logger.LogDebug("Dequeued message from Azure Queue: {QueueName}", queueName);

            return new QueueMessage<T>
            {
                Content = content,
                MessageId = azureMessage.MessageId,
                PopReceipt = azureMessage.PopReceipt,
                DequeueCount = (int)azureMessage.DequeueCount,
                InsertedOn = azureMessage.InsertedOn?.UtcDateTime
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deserializing message from Azure Queue: {QueueName}", queueName);
            return null;
        }
    }

    /// <inheritdoc/>
    public async Task<T?> PeekAsync<T>(
        string queueName, 
        CancellationToken cancellationToken = default) where T : class
    {
        var queueClient = await GetQueueClientAsync(queueName, cancellationToken);
        
        var response = await queueClient.PeekMessageAsync(cancellationToken: cancellationToken);

        if (response.Value is null)
        {
            return null;
        }

        try
        {
            var json = Encoding.UTF8.GetString(Convert.FromBase64String(response.Value.MessageText));
            return JsonSerializer.Deserialize<T>(json);
        }
        catch
        {
            return null;
        }
    }

    /// <inheritdoc/>
    public async Task DeleteMessageAsync(
        string queueName, 
        string messageId, 
        string popReceipt,
        CancellationToken cancellationToken = default)
    {
        var queueClient = await GetQueueClientAsync(queueName, cancellationToken);
        await queueClient.DeleteMessageAsync(messageId, popReceipt, cancellationToken);
        
        _logger.LogDebug("Deleted message from Azure Queue: {MessageId} in {QueueName}", messageId, queueName);
    }

    /// <inheritdoc/>
    public async Task<int> GetQueueLengthAsync(
        string queueName, 
        CancellationToken cancellationToken = default)
    {
        var queueClient = await GetQueueClientAsync(queueName, cancellationToken);
        var properties = await queueClient.GetPropertiesAsync(cancellationToken);
        return properties.Value.ApproximateMessagesCount;
    }

    /// <inheritdoc/>
    public async Task ClearQueueAsync(
        string queueName, 
        CancellationToken cancellationToken = default)
    {
        var queueClient = await GetQueueClientAsync(queueName, cancellationToken);
        await queueClient.ClearMessagesAsync(cancellationToken);
        
        _logger.LogDebug("Cleared Azure Queue: {QueueName}", queueName);
    }
}

/// <summary>
/// Configuration options for Azure Queue Storage
/// </summary>
public class AzureQueueStorageOptions
{
    /// <summary>
    /// Azure Storage connection string
    /// TODO: Set via environment variable AZURE_STORAGE_CONNECTION_STRING
    /// </summary>
    public string? ConnectionString { get; set; }
}
