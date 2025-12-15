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

        // Configure base URL
        _httpClient.BaseAddress = new Uri(_options.OllamaUrl);
        _httpClient.Timeout = TimeSpan.FromSeconds(_options.TimeoutSeconds);

        _logger.LogInformation("OllamaLLMProvider initialized with model: {Model} at {Url}", 
            _options.LLMModel, _options.OllamaUrl);
    }

    /// <inheritdoc/>
    public async Task<Result<LLMResponse>> AnalyzeAsync(
        LLMRequest request,
        CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();

        try
        {
            _logger.LogDebug("Sending LLM request to Ollama (model: {Model})", _options.LLMModel);

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

                    httpResponse.EnsureSuccessStatusCode();
                    return await httpResponse.Content.ReadFromJsonAsync<OllamaResponse>(ct)
                        ?? throw new InvalidOperationException("Empty response from Ollama");
                },
                cancellationToken);

            stopwatch.Stop();

            _logger.LogInformation("LLM request completed in {Duration}ms", stopwatch.ElapsedMilliseconds);

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
            _logger.LogError("LLM request timed out after {Timeout}s", _options.TimeoutSeconds);
            return Result<LLMResponse>.Failure(new Error(
                "LLM.Timeout",
                $"Request timed out after {_options.TimeoutSeconds} seconds"));
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

        try
        {
            _logger.LogDebug("Sending streaming LLM request to Ollama (model: {Model})", _options.LLMModel);

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

            httpResponse.EnsureSuccessStatusCode();

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
            _logger.LogWarning("Streaming LLM request cancelled");
            return Result<LLMResponse>.Failure(new Error(
                "LLM.Cancelled",
                "Request was cancelled"));
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
