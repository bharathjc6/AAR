// =============================================================================
// AAR.Infrastructure - Services/Resilience/ResiliencePipelines.cs
// Polly resilience policies for external API calls
// =============================================================================

using AAR.Application.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Polly;
using Polly.CircuitBreaker;
using Polly.Retry;
using Polly.Timeout;
using PollyTimeoutStrategyOptions = Polly.Timeout.TimeoutStrategyOptions;

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
    public const string LLMProvider = "LLMProvider";
    public const string EmbeddingProvider = "EmbeddingProvider";
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
                .AddTimeout(new PollyTimeoutStrategyOptions
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

        // LLM Provider pipeline (for Ollama/local LLM)
        services.AddResiliencePipeline(ResiliencePipelineNames.LLMProvider, (builder, context) =>
        {
            var logger = context.ServiceProvider.GetRequiredService<ILogger<ResiliencePipelineBuilder>>();
            var aiOptions = context.ServiceProvider.GetRequiredService<IOptions<AIProviderOptions>>().Value;
            var localOptions = aiOptions.Local;
            var timeoutStrategy = localOptions.TimeoutStrategy;

            // Use adaptive timeout strategy: base + per-token calculation
            // This allows longer timeouts for larger requests while keeping small requests fast
            builder
                .AddTimeout(new PollyTimeoutStrategyOptions
                {
                    // Use the max timeout from adaptive strategy config (Polly uses this as the base)
                    Timeout = TimeSpan.FromSeconds(timeoutStrategy.MaxTimeoutSeconds)
                })
                .AddRetry(new RetryStrategyOptions
                {
                    MaxRetryAttempts = 2, // Reduced from 3 to avoid cascading timeouts
                    Delay = TimeSpan.FromSeconds(2),
                    BackoffType = DelayBackoffType.Exponential,
                    UseJitter = true,
                    ShouldHandle = new PredicateBuilder()
                        .Handle<HttpRequestException>()
                        .Handle<TimeoutRejectedException>(ex => 
                            !ex.Message.Contains("didn't complete")) // Don't retry actual timeouts
                        .Handle<TaskCanceledException>(ex => !ex.CancellationToken.IsCancellationRequested),
                    OnRetry = args =>
                    {
                        logger.LogWarning(
                            "Retry {Attempt}/2 for LLM provider: {Exception}. " +
                            "This may indicate slow inference or high load on the LLM service.",
                            args.AttemptNumber,
                            args.Outcome.Exception?.Message);
                        return default;
                    }
                })
                .AddCircuitBreaker(new CircuitBreakerStrategyOptions
                {
                    FailureRatio = 0.5,
                    SamplingDuration = TimeSpan.FromMinutes(2),
                    MinimumThroughput = 3,
                    BreakDuration = TimeSpan.FromSeconds(60),
                    ShouldHandle = new PredicateBuilder()
                        .Handle<HttpRequestException>()
                        .Handle<TimeoutRejectedException>(),
                    OnOpened = args =>
                    {
                        logger.LogError("Circuit breaker OPENED for LLM provider - LLM appears to be unresponsive");
                        return default;
                    },
                    OnClosed = args =>
                    {
                        logger.LogInformation("Circuit breaker CLOSED for LLM provider");
                        return default;
                    }
                });
        });

        // Embedding Provider pipeline (for Ollama embeddings)
        services.AddResiliencePipeline(ResiliencePipelineNames.EmbeddingProvider, (builder, context) =>
        {
            var logger = context.ServiceProvider.GetRequiredService<ILogger<ResiliencePipelineBuilder>>();

            builder
                .AddTimeout(TimeSpan.FromSeconds(120)) // Embeddings timeout
                .AddRetry(new RetryStrategyOptions
                {
                    MaxRetryAttempts = 3,
                    Delay = TimeSpan.FromSeconds(1),
                    BackoffType = DelayBackoffType.Exponential,
                    UseJitter = true,
                    ShouldHandle = new PredicateBuilder()
                        .Handle<HttpRequestException>()
                        .Handle<TimeoutRejectedException>()
                        .Handle<TaskCanceledException>(ex => !ex.CancellationToken.IsCancellationRequested),
                    OnRetry = args =>
                    {
                        logger.LogWarning(
                            "Retry {Attempt}/3 for embedding provider: {Exception}",
                            args.AttemptNumber,
                            args.Outcome.Exception?.Message);
                        return default;
                    }
                })
                .AddCircuitBreaker(new CircuitBreakerStrategyOptions
                {
                    FailureRatio = 0.5,
                    SamplingDuration = TimeSpan.FromSeconds(30),
                    MinimumThroughput = 10,
                    BreakDuration = TimeSpan.FromSeconds(15),
                    ShouldHandle = new PredicateBuilder()
                        .Handle<HttpRequestException>()
                        .Handle<TimeoutRejectedException>(),
                    OnOpened = args =>
                    {
                        logger.LogError("Circuit breaker OPENED for embedding provider");
                        return default;
                    },
                    OnClosed = args =>
                    {
                        logger.LogInformation("Circuit breaker CLOSED for embedding provider");
                        return default;
                    }
                });
        });

        return services;
    }
}
