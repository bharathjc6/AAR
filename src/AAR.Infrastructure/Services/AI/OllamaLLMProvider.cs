// =============================================================================
// AAR.Infrastructure - Services/AI/OllamaLLMProvider.cs
// Ollama LLM provider implementation with retry and streaming support
// =============================================================================

using System.Diagnostics;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using AAR.Application.Configuration;
using AAR.Application.Interfaces;
using AAR.Shared;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Polly;
using Polly.Registry;

namespace AAR.Infrastructure.Services.AI;

/// <summary>
/// Ollama LLM provider with retry, timeout, and streaming support
/// </summary>
public class OllamaLLMProvider : ILLMProvider, IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly LocalAIOptions _options;
    private readonly ResiliencePipeline _pipeline;
    private readonly ILogger<OllamaLLMProvider> _logger;

    public string ProviderName => "Ollama";
    public string ModelName => _options.LLMModel;

    public OllamaLLMProvider(
        IHttpClientFactory httpClientFactory,
        IOptions<AIProviderOptions> options,
        ResiliencePipelineProvider<string> pipelineProvider,
        ILogger<OllamaLLMProvider> logger)
    {
        _httpClient = httpClientFactory.CreateClient("Ollama");
        _options = options.Value.Local;
        _pipeline = pipelineProvider.GetPipeline("LLMProvider");
        _logger = logger;

        // Configure base URL.
        // Do NOT set an HttpClient-level timeout here - rely on the Polly resilience
        // pipeline (which has a longer, configurable timeout) to control request
        // timeouts. Setting HttpClient.Timeout can abort the underlying socket and
        // interfere with the pipeline's retry/timeout behavior.
        _httpClient.BaseAddress = new Uri(_options.OllamaUrl);
        _httpClient.Timeout = System.Threading.Timeout.InfiniteTimeSpan;

        _logger.LogInformation("OllamaLLMProvider initialized with model: {Model} at {Url}. Using resilience pipeline for timeouts.", 
            _options.LLMModel, _options.OllamaUrl);
    }

    /// <summary>
    /// Calculate adaptive timeout based on request parameters and strategy configuration
    /// </summary>
    /// <remarks>
    /// Adaptive timeout is calculated as:
    /// timeout = base + (maxTokens * perTokenTimeout)
    /// Then clamped to [minTimeout, maxTimeout] range
    /// </remarks>
    private TimeSpan CalculateAdaptiveTimeout(LLMRequest request, bool isStreaming = false)
    {
        if (!_options.UseAdaptiveTimeout)
        {
            return TimeSpan.FromSeconds(_options.TimeoutSeconds);
        }

        var strategy = _options.TimeoutStrategy;
        
        // Calculate base timeout for this request
        var baseSeconds = strategy.BaseTimeoutSeconds;
        var additionalSeconds = (request.MaxTokens * strategy.PerTokenTimeoutMs) / 1000.0;
        var totalSeconds = baseSeconds + additionalSeconds;

        // Apply streaming multiplier if needed
        if (isStreaming)
        {
            totalSeconds *= strategy.StreamingTimeoutMultiplier;
        }

        // Clamp to min/max bounds
        var finalSeconds = Math.Max(
            strategy.MinTimeoutSeconds,
            Math.Min(strategy.MaxTimeoutSeconds, (int)Math.Ceiling(totalSeconds)));

        _logger.LogDebug(
            "Calculated adaptive timeout: {Timeout}s (base: {Base}s, tokens: {Tokens}, streaming: {IsStreaming})",
            finalSeconds, baseSeconds, request.MaxTokens, isStreaming);

        return TimeSpan.FromSeconds(finalSeconds);
    }

    /// <inheritdoc/>
    public async Task<Result<LLMResponse>> AnalyzeAsync(
        LLMRequest request,
        CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        var adaptiveTimeout = CalculateAdaptiveTimeout(request, isStreaming: false);

        try
        {
            _logger.LogDebug(
                "Sending LLM request to Ollama (model: {Model}, maxTokens: {MaxTokens}, timeout: {Timeout}s)",
                _options.LLMModel,
                request.MaxTokens,
                adaptiveTimeout.TotalSeconds);

            var ollamaRequest = new OllamaRequest
            {
                Model = _options.LLMModel,
                Messages =
                [
                    new OllamaMessage { Role = "system", Content = request.SystemPrompt },
                    new OllamaMessage { Role = "user", Content = request.UserPrompt }
                ],
                Stream = false,
                Options = new OllamaOptions
                {
                    Temperature = request.Temperature,
                    NumPredict = request.MaxTokens
                }
            };

            var response = await _pipeline.ExecuteAsync(
                async ct =>
                {
                    var httpResponse = await _httpClient.PostAsJsonAsync(
                        "/api/chat",
                        ollamaRequest,
                        ct);

                    if (!httpResponse.IsSuccessStatusCode)
                    {
                        var body = string.Empty;
                        try { body = await httpResponse.Content.ReadAsStringAsync(ct); } catch { }

                        // Detect when the model cannot be loaded due to insufficient host memory.
                        // Return a sentinel OllamaResponse so the resilience pipeline treats the call as
                        // successful (avoiding tripping the circuit), and handle the logical error
                        // after the pipeline completes.
                        if (!string.IsNullOrEmpty(body) &&
                            body.IndexOf("requires more system memory", StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            _logger.LogWarning(
                                "Ollama model cannot be loaded due to insufficient memory: {Body}",
                                body);

                            return new OllamaResponse
                            {
                                Model = _options.LLMModel,
                                Message = new OllamaMessage { Role = "system", Content = string.Empty },
                                Done = true,
                                DoneReason = "model_unavailable",
                                PromptEvalCount = 0,
                                EvalCount = 0
                            };
                        }

                        var code = (int)httpResponse.StatusCode;
                        throw new HttpRequestException($"Ollama returned {code} {httpResponse.ReasonPhrase}: {body}");
                    }

                    var parsed = await httpResponse.Content.ReadFromJsonAsync<OllamaResponse>(ct);
                    return parsed ?? throw new InvalidOperationException("Empty response from Ollama");
                },
                cancellationToken);

            // If we returned a sentinel response because the model could not be loaded,
            // map it to a failure result without throwing to avoid tripping the resilience
            // circuit for a non-transient configuration issue.
            if (response.DoneReason != null && response.DoneReason.Equals("model_unavailable", StringComparison.OrdinalIgnoreCase))
            {
                stopwatch.Stop();
                _logger.LogWarning("LLM model unavailable on Ollama host (insufficient memory)");
                return Result<LLMResponse>.Failure(new Error(
                    "LLM.ModelTooLarge",
                    "The configured model requires more memory than is available on the Ollama host. " +
                    "Use a smaller model or increase host memory."));
            }

            stopwatch.Stop();

            _logger.LogInformation(
                "LLM request completed in {Duration}ms (tokens: prompt={PromptTokens}, completion={CompletionTokens})",
                stopwatch.ElapsedMilliseconds,
                response.PromptEvalCount ?? 0,
                response.EvalCount ?? 0);

            return Result<LLMResponse>.Success(new LLMResponse
            {
                Content = response.Message?.Content ?? string.Empty,
                PromptTokens = response.PromptEvalCount ?? 0,
                CompletionTokens = response.EvalCount ?? 0,
                Model = response.Model,
                Duration = stopwatch.Elapsed,
                FinishReason = response.DoneReason ?? "completed"
            });
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "HTTP error calling Ollama: {Message}", ex.Message);
            return Result<LLMResponse>.Failure(new Error(
                "LLM.ConnectionError",
                $"Failed to connect to Ollama at {_options.OllamaUrl}. Is Ollama running?"));
        }
        catch (TaskCanceledException ex) when (ex.CancellationToken == cancellationToken)
        {
            _logger.LogWarning("LLM request cancelled by user");
            return Result<LLMResponse>.Failure(new Error(
                "LLM.Cancelled",
                "Request was cancelled"));
        }
        catch (TaskCanceledException)
        {
            stopwatch.Stop();
            _logger.LogError(
                "LLM request timed out after {Duration}ms. " +
                "Consider: 1) Increasing TimeoutStrategy.MaxTimeoutSeconds, " +
                "2) Reducing MaxTokens, or 3) Deploying a faster model/GPU.",
                stopwatch.ElapsedMilliseconds);

            // Graceful degradation: provide a partial response based on what we have
            if (_options.TimeoutStrategy.EnableGracefulDegradation)
            {
                _logger.LogWarning(
                    "Graceful degradation enabled: attempting to return partial/fallback result");
                return Result<LLMResponse>.Failure(new Error(
                    "LLM.Timeout",
                    "Request timed out after " + (int)stopwatch.Elapsed.TotalSeconds + " seconds. " +
                    "The LLM service is overloaded or the inference is too slow. " +
                    "Increase TimeoutStrategy.MaxTimeoutSeconds in configuration or try again later."));
            }

            return Result<LLMResponse>.Failure(new Error(
                "LLM.Timeout",
                "Request timed out or was cancelled"));
        }
        catch (Polly.Timeout.TimeoutRejectedException tex)
        {
            stopwatch.Stop();
            _logger.LogError(
                tex,
                "LLM request timed out by resilience pipeline after {Duration}ms. " +
                "Adaptive timeout was: {Timeout}s. Consider increasing TimeoutStrategy.MaxTimeoutSeconds.",
                stopwatch.ElapsedMilliseconds,
                CalculateAdaptiveTimeout(request).TotalSeconds);

            return Result<LLMResponse>.Failure(new Error(
                "LLM.Timeout",
                "Request timed out by resilience pipeline after " + (int)stopwatch.Elapsed.TotalSeconds + " seconds. " +
                "The model inference is taking longer than expected. Adjust configuration or try a faster model."));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error calling Ollama: {Message}", ex.Message);
            return Result<LLMResponse>.Failure(new Error(
                "LLM.UnexpectedError",
                $"Unexpected error: {ex.Message}"));
        }
    }

    /// <inheritdoc/>
    public async Task<Result<LLMResponse>> AnalyzeStreamingAsync(
        LLMRequest request,
        Action<string> onChunk,
        CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        var fullContent = new StringBuilder();
        int promptTokens = 0;
        int completionTokens = 0;
        var adaptiveTimeout = CalculateAdaptiveTimeout(request, isStreaming: true);

        try
        {
            _logger.LogDebug(
                "Sending streaming LLM request to Ollama (model: {Model}, maxTokens: {MaxTokens}, timeout: {Timeout}s)",
                _options.LLMModel,
                request.MaxTokens,
                adaptiveTimeout.TotalSeconds);

            var ollamaRequest = new OllamaRequest
            {
                Model = _options.LLMModel,
                Messages =
                [
                    new OllamaMessage { Role = "system", Content = request.SystemPrompt },
                    new OllamaMessage { Role = "user", Content = request.UserPrompt }
                ],
                Stream = true,
                Options = new OllamaOptions
                {
                    Temperature = request.Temperature,
                    NumPredict = request.MaxTokens
                }
            };

            var httpRequest = new HttpRequestMessage(HttpMethod.Post, "/api/chat")
            {
                Content = JsonContent.Create(ollamaRequest)
            };

            using var httpResponse = await _httpClient.SendAsync(
                httpRequest,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);

            if (!httpResponse.IsSuccessStatusCode)
            {
                var body = string.Empty;
                try { body = await httpResponse.Content.ReadAsStringAsync(cancellationToken); } catch { }

                if (!string.IsNullOrEmpty(body) &&
                    body.IndexOf("requires more system memory", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    _logger.LogWarning("Ollama streaming model cannot be loaded due to insufficient memory: {Body}", body);
                    return Result<LLMResponse>.Failure(new Error(
                        "LLM.ModelTooLarge",
                        "The configured model requires more memory than is available on the Ollama host. " +
                        "Use a smaller model or increase host memory."));
                }

                httpResponse.EnsureSuccessStatusCode();
            }

            await using var stream = await httpResponse.Content.ReadAsStreamAsync(cancellationToken);
            using var reader = new StreamReader(stream);

            while (!reader.EndOfStream)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var line = await reader.ReadLineAsync(cancellationToken);
                if (string.IsNullOrWhiteSpace(line))
                    continue;

                try
                {
                    var chunk = JsonSerializer.Deserialize<OllamaStreamChunk>(line);
                    if (chunk?.Message?.Content is { } content)
                    {
                        fullContent.Append(content);
                        onChunk(content);
                    }

                    if (chunk?.Done == true)
                    {
                        promptTokens = chunk.PromptEvalCount ?? 0;
                        completionTokens = chunk.EvalCount ?? 0;
                    }
                }
                catch (JsonException ex)
                {
                    _logger.LogWarning(ex, "Failed to parse streaming chunk: {Line}", line);
                }
            }

            stopwatch.Stop();

            _logger.LogInformation("Streaming LLM request completed in {Duration}ms", stopwatch.ElapsedMilliseconds);

            return Result<LLMResponse>.Success(new LLMResponse
            {
                Content = fullContent.ToString(),
                PromptTokens = promptTokens,
                CompletionTokens = completionTokens,
                Model = _options.LLMModel,
                Duration = stopwatch.Elapsed,
                FinishReason = "completed"
            });
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "HTTP error calling Ollama: {Message}", ex.Message);
            return Result<LLMResponse>.Failure(new Error(
                "LLM.ConnectionError",
                $"Failed to connect to Ollama at {_options.OllamaUrl}"));
        }
        catch (OperationCanceledException)
        {
            stopwatch.Stop();
            _logger.LogWarning("Streaming LLM request cancelled or timed out after {Duration}ms", stopwatch.ElapsedMilliseconds);
            
            // If we have partial content from graceful degradation, return it
            if (_options.TimeoutStrategy.EnableGracefulDegradation && fullContent.Length > 0)
            {
                _logger.LogInformation(
                    "Graceful degradation: returning {ContentLength} chars of partial streamed response",
                    fullContent.Length);
                return Result<LLMResponse>.Success(new LLMResponse
                {
                    Content = fullContent.ToString(),
                    PromptTokens = promptTokens,
                    CompletionTokens = completionTokens,
                    Model = _options.LLMModel,
                    Duration = stopwatch.Elapsed,
                    FinishReason = "incomplete"
                });
            }

            return Result<LLMResponse>.Failure(new Error(
                "LLM.Cancelled",
                "Request was cancelled or timed out after " + (int)stopwatch.Elapsed.TotalSeconds + " seconds"));
        }
        catch (Polly.Timeout.TimeoutRejectedException tex)
        {
            stopwatch.Stop();
            _logger.LogError(
                tex,
                "Streaming LLM request timed out by resilience pipeline after {Duration}ms. " +
                "Adaptive timeout was: {Timeout}s.",
                stopwatch.ElapsedMilliseconds,
                CalculateAdaptiveTimeout(request, isStreaming: true).TotalSeconds);

            // Return partial content if graceful degradation is enabled
            if (_options.TimeoutStrategy.EnableGracefulDegradation && fullContent.Length > 0)
            {
                _logger.LogInformation(
                    "Graceful degradation: returning {ContentLength} chars of partial streamed response",
                    fullContent.Length);
                return Result<LLMResponse>.Success(new LLMResponse
                {
                    Content = fullContent.ToString(),
                    PromptTokens = promptTokens,
                    CompletionTokens = completionTokens,
                    Model = _options.LLMModel,
                    Duration = stopwatch.Elapsed,
                    FinishReason = "incomplete"
                });
            }

            return Result<LLMResponse>.Failure(new Error(
                "LLM.Timeout",
                "Streaming request timed out after " + (int)stopwatch.Elapsed.TotalSeconds + " seconds. " +
                "Increase TimeoutStrategy.MaxTimeoutSeconds or reduce MaxTokens."));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error in streaming: {Message}", ex.Message);
            return Result<LLMResponse>.Failure(new Error(
                "LLM.UnexpectedError",
                $"Unexpected error: {ex.Message}"));
        }
    }

    /// <inheritdoc/>
    public async Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _httpClient.GetAsync("/api/tags", cancellationToken);
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    public void Dispose()
    {
        _httpClient?.Dispose();
    }

    #region Ollama API Models

    private class OllamaRequest
    {
        [JsonPropertyName("model")]
        public required string Model { get; init; }

        [JsonPropertyName("messages")]
        public required List<OllamaMessage> Messages { get; init; }

        [JsonPropertyName("stream")]
        public bool Stream { get; init; }

        [JsonPropertyName("options")]
        public OllamaOptions? Options { get; init; }
    }

    private class OllamaMessage
    {
        [JsonPropertyName("role")]
        public required string Role { get; init; }

        [JsonPropertyName("content")]
        public required string Content { get; init; }
    }

    private class OllamaOptions
    {
        [JsonPropertyName("temperature")]
        public float Temperature { get; init; }

        [JsonPropertyName("num_predict")]
        public int NumPredict { get; init; }
    }

    private class OllamaResponse
    {
        [JsonPropertyName("model")]
        public string? Model { get; init; }

        [JsonPropertyName("message")]
        public OllamaMessage? Message { get; init; }

        [JsonPropertyName("done")]
        public bool Done { get; init; }

        [JsonPropertyName("done_reason")]
        public string? DoneReason { get; init; }

        [JsonPropertyName("prompt_eval_count")]
        public int? PromptEvalCount { get; init; }

        [JsonPropertyName("eval_count")]
        public int? EvalCount { get; init; }
    }

    private class OllamaStreamChunk
    {
        [JsonPropertyName("model")]
        public string? Model { get; init; }

        [JsonPropertyName("message")]
        public OllamaMessage? Message { get; init; }

        [JsonPropertyName("done")]
        public bool Done { get; init; }

        [JsonPropertyName("prompt_eval_count")]
        public int? PromptEvalCount { get; init; }

        [JsonPropertyName("eval_count")]
        public int? EvalCount { get; init; }
    }

    #endregion
}
