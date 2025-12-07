// =============================================================================
// AAR.Infrastructure - Messaging/MassTransitConfiguration.cs
// MassTransit configuration for In-Memory and Azure Service Bus transports
// =============================================================================

using AAR.Application.Messaging;
using MassTransit;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace AAR.Infrastructure.Messaging;

/// <summary>
/// MassTransit configuration options
/// </summary>
public class MassTransitOptions
{
    public const string SectionName = "MassTransit";
    
    /// <summary>
    /// Transport type: "InMemory" or "AzureServiceBus"
    /// </summary>
    public string Transport { get; set; } = "InMemory";
    
    /// <summary>
    /// Azure Service Bus connection string (required when Transport is AzureServiceBus)
    /// </summary>
    public string? AzureServiceBusConnectionString { get; set; }
    
    /// <summary>
    /// Enable scheduled message redelivery
    /// </summary>
    public bool EnableScheduledRedelivery { get; set; } = true;
    
    /// <summary>
    /// Number of concurrent consumers
    /// </summary>
    public int ConcurrentMessageLimit { get; set; } = 5;
    
    /// <summary>
    /// Enable message retry
    /// </summary>
    public bool EnableRetry { get; set; } = true;
    
    /// <summary>
    /// Maximum retry attempts
    /// </summary>
    public int MaxRetryAttempts { get; set; } = 3;
    
    /// <summary>
    /// Whether to register consumers (false for API, true for Worker)
    /// </summary>
    public bool RegisterConsumers { get; set; } = false;
}

/// <summary>
/// Extension methods for configuring MassTransit
/// </summary>
public static class MassTransitConfiguration
{
    /// <summary>
    /// Adds MassTransit messaging to the service collection (Publisher only - for API)
    /// </summary>
    public static IServiceCollection AddMassTransitPublisher(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var options = configuration.GetSection(MassTransitOptions.SectionName).Get<MassTransitOptions>() 
            ?? new MassTransitOptions();

        // Check for environment variable override
        var connectionString = Environment.GetEnvironmentVariable("AZURE_SERVICEBUS_CONNECTION_STRING")
            ?? options.AzureServiceBusConnectionString;

        if (!string.IsNullOrEmpty(connectionString))
        {
            options.Transport = "AzureServiceBus";
            options.AzureServiceBusConnectionString = connectionString;
        }

        services.AddMassTransit(x =>
        {
            x.SetKebabCaseEndpointNameFormatter();

            if (options.Transport == "AzureServiceBus" && !string.IsNullOrEmpty(options.AzureServiceBusConnectionString))
            {
                x.UsingAzureServiceBus((context, cfg) =>
                {
                    cfg.Host(options.AzureServiceBusConnectionString);
                    
                    // Don't configure endpoints - this is publisher only
                    // Endpoints will be auto-created when publishing
                });
            }
            else
            {
                x.UsingInMemory((context, cfg) =>
                {
                    cfg.ConfigureEndpoints(context);
                });
            }
        });

        // Register message bus abstraction
        services.AddScoped<IMessageBus, MassTransitMessageBus>();

        return services;
    }

    /// <summary>
    /// Adds MassTransit messaging with consumers to the service collection (for Worker)
    /// </summary>
    public static IServiceCollection AddMassTransitWithConsumers(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var options = configuration.GetSection(MassTransitOptions.SectionName).Get<MassTransitOptions>() 
            ?? new MassTransitOptions();

        // Check for environment variable override
        var connectionString = Environment.GetEnvironmentVariable("AZURE_SERVICEBUS_CONNECTION_STRING")
            ?? options.AzureServiceBusConnectionString;

        if (!string.IsNullOrEmpty(connectionString))
        {
            options.Transport = "AzureServiceBus";
            options.AzureServiceBusConnectionString = connectionString;
        }

        services.AddMassTransit(x =>
        {
            x.SetKebabCaseEndpointNameFormatter();

            // Register consumers
            x.AddConsumer<StartAnalysisConsumer, StartAnalysisConsumerDefinition>();

            if (options.Transport == "AzureServiceBus" && !string.IsNullOrEmpty(options.AzureServiceBusConnectionString))
            {
                x.UsingAzureServiceBus((context, cfg) =>
                {
                    cfg.Host(options.AzureServiceBusConnectionString);

                    // Configure receive endpoint for analysis jobs
                    cfg.ReceiveEndpoint("analysis-jobs", e =>
                    {
                        e.ConfigureConsumer<StartAnalysisConsumer>(context);
                        
                        // Configure prefetch and concurrency
                        e.PrefetchCount = options.ConcurrentMessageLimit * 2;
                        e.ConcurrentMessageLimit = options.ConcurrentMessageLimit;
                        
                        // Configure dead-letter
                        e.MaxDeliveryCount = options.MaxRetryAttempts + 1;
                    });
                });
            }
            else
            {
                x.UsingInMemory((context, cfg) =>
                {
                    cfg.ReceiveEndpoint("analysis-jobs", e =>
                    {
                        e.ConfigureConsumer<StartAnalysisConsumer>(context);
                        e.ConcurrentMessageLimit = options.ConcurrentMessageLimit;
                    });
                    
                    cfg.ConfigureEndpoints(context);
                });
            }
        });

        // Register message bus abstraction
        services.AddScoped<IMessageBus, MassTransitMessageBus>();

        return services;
    }
    
    /// <summary>
    /// Unified method for adding MassTransit messaging.
    /// Uses RegisterConsumers option to determine whether to register consumers.
    /// </summary>
    public static IServiceCollection AddMassTransitMessaging(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var options = configuration.GetSection(MassTransitOptions.SectionName).Get<MassTransitOptions>() 
            ?? new MassTransitOptions();
        
        // Check environment variable for consumer registration
        var registerConsumers = Environment.GetEnvironmentVariable("MASSTRANSIT_REGISTER_CONSUMERS");
        if (!string.IsNullOrEmpty(registerConsumers))
        {
            options.RegisterConsumers = bool.TryParse(registerConsumers, out var val) && val;
        }
        
        if (options.RegisterConsumers)
        {
            return services.AddMassTransitWithConsumers(configuration);
        }
        else
        {
            return services.AddMassTransitPublisher(configuration);
        }
    }
}
