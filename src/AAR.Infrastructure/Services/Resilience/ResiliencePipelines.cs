// =============================================================================
// AAR.Infrastructure - Services/Resilience/ResiliencePipelines.cs
// Polly resilience policies for external API calls
// =============================================================================

using AAR.Application.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Polly;
using Polly.CircuitBreaker;
using Polly.Retry;
using Polly.Timeout;

namespace AAR.Infrastructure.Services.Resilience;

/// <summary>
/// Named resilience pipelines for different scenarios
/// </summary>
public static class ResiliencePipelineNames
{
    public const string EmbeddingApi = "EmbeddingApi";
    public const string OpenAiApi = "OpenAiApi";
    public const string BlobStorage = "BlobStorage";
    public const string Database = "Database";
}

/// <summary>
/// Extension methods for configuring Polly resilience
/// </summary>
public static class ResilienceServiceCollectionExtensions
{
    /// <summary>
    /// Adds Polly resilience pipelines to the service collection
    /// </summary>
    public static IServiceCollection AddResiliencePolicies(
        this IServiceCollection services)
    {
        services.AddResiliencePipeline(ResiliencePipelineNames.EmbeddingApi, (builder, context) =>
        {
            var options = context.ServiceProvider.GetRequiredService<IOptions<EmbeddingProcessingOptions>>().Value;
            var logger = context.ServiceProvider.GetRequiredService<ILogger<ResiliencePipelineBuilder>>();

            builder
                // Timeout for individual embedding calls
                .AddTimeout(new TimeoutStrategyOptions
                {
                    Timeout = TimeSpan.FromSeconds(60),
                    OnTimeout = args =>
                    {
                        logger.LogWarning("Embedding API call timed out after 60s");
                        return default;
                    }
                })
                // Retry with exponential backoff
                .AddRetry(new RetryStrategyOptions
                {
                    MaxRetryAttempts = options.MaxRetryAttempts,
                    Delay = TimeSpan.FromMilliseconds(options.RetryBaseDelayMs),
                    BackoffType = DelayBackoffType.Exponential,
                    UseJitter = true,
                    ShouldHandle = new PredicateBuilder()
                        .Handle<HttpRequestException>()
                        .Handle<TimeoutRejectedException>()
                        .Handle<TaskCanceledException>(ex => !ex.CancellationToken.IsCancellationRequested),
                    OnRetry = args =>
                    {
                        logger.LogWarning(
                            "Retry {Attempt}/{MaxAttempts} for embedding API after {Delay}ms: {Exception}",
                            args.AttemptNumber,
                            options.MaxRetryAttempts,
                            args.RetryDelay.TotalMilliseconds,
                            args.Outcome.Exception?.Message);
                        return default;
                    }
                })
                // Circuit breaker
                .AddCircuitBreaker(new CircuitBreakerStrategyOptions
                {
                    FailureRatio = 0.5,
                    SamplingDuration = TimeSpan.FromSeconds(30),
                    MinimumThroughput = options.CircuitBreakerFailureThreshold,
                    BreakDuration = TimeSpan.FromSeconds(options.CircuitBreakerBreakDurationSeconds),
                    ShouldHandle = new PredicateBuilder()
                        .Handle<HttpRequestException>()
                        .Handle<TimeoutRejectedException>(),
                    OnOpened = args =>
                    {
                        logger.LogError(
                            "Circuit breaker OPENED for embedding API. Will retry in {Duration}s",
                            options.CircuitBreakerBreakDurationSeconds);
                        return default;
                    },
                    OnClosed = args =>
                    {
                        logger.LogInformation("Circuit breaker CLOSED for embedding API");
                        return default;
                    },
                    OnHalfOpened = args =>
                    {
                        logger.LogInformation("Circuit breaker HALF-OPEN for embedding API");
                        return default;
                    }
                });
        });

        services.AddResiliencePipeline(ResiliencePipelineNames.OpenAiApi, (builder, context) =>
        {
            var logger = context.ServiceProvider.GetRequiredService<ILogger<ResiliencePipelineBuilder>>();

            builder
                .AddTimeout(TimeSpan.FromMinutes(2)) // Longer timeout for reasoning
                .AddRetry(new RetryStrategyOptions
                {
                    MaxRetryAttempts = 3,
                    Delay = TimeSpan.FromSeconds(2),
                    BackoffType = DelayBackoffType.Exponential,
                    UseJitter = true,
                    ShouldHandle = new PredicateBuilder()
                        .Handle<HttpRequestException>()
                        .Handle<TimeoutRejectedException>(),
                    OnRetry = args =>
                    {
                        logger.LogWarning(
                            "Retry {Attempt}/3 for OpenAI API: {Exception}",
                            args.AttemptNumber,
                            args.Outcome.Exception?.Message);
                        return default;
                    }
                })
                .AddCircuitBreaker(new CircuitBreakerStrategyOptions
                {
                    FailureRatio = 0.5,
                    SamplingDuration = TimeSpan.FromMinutes(1),
                    MinimumThroughput = 10,
                    BreakDuration = TimeSpan.FromMinutes(1),
                    OnOpened = args =>
                    {
                        logger.LogError("Circuit breaker OPENED for OpenAI API");
                        return default;
                    }
                });
        });

        services.AddResiliencePipeline(ResiliencePipelineNames.BlobStorage, (builder, context) =>
        {
            var logger = context.ServiceProvider.GetRequiredService<ILogger<ResiliencePipelineBuilder>>();

            builder
                .AddTimeout(TimeSpan.FromSeconds(30))
                .AddRetry(new RetryStrategyOptions
                {
                    MaxRetryAttempts = 3,
                    Delay = TimeSpan.FromMilliseconds(500),
                    BackoffType = DelayBackoffType.Exponential,
                    OnRetry = args =>
                    {
                        logger.LogWarning(
                            "Retry {Attempt}/3 for blob storage: {Exception}",
                            args.AttemptNumber,
                            args.Outcome.Exception?.Message);
                        return default;
                    }
                });
        });

        services.AddResiliencePipeline(ResiliencePipelineNames.Database, (builder, context) =>
        {
            var logger = context.ServiceProvider.GetRequiredService<ILogger<ResiliencePipelineBuilder>>();

            builder
                .AddRetry(new RetryStrategyOptions
                {
                    MaxRetryAttempts = 3,
                    Delay = TimeSpan.FromMilliseconds(100),
                    BackoffType = DelayBackoffType.Exponential,
                    OnRetry = args =>
                    {
                        logger.LogWarning(
                            "Retry {Attempt}/3 for database: {Exception}",
                            args.AttemptNumber,
                            args.Outcome.Exception?.Message);
                        return default;
                    }
                });
        });

        return services;
    }
}
