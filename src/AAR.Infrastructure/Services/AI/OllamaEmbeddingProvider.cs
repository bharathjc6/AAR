// =============================================================================
// AAR.Infrastructure - Services/AI/OllamaEmbeddingProvider.cs
// BGE embedding provider via Ollama with batch support
// =============================================================================

using System.Net.Http.Json;
using System.Text.Json.Serialization;
using AAR.Application.Configuration;
using AAR.Application.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Polly;
using Polly.Registry;

namespace AAR.Infrastructure.Services.AI;

/// <summary>
/// BGE embedding provider via Ollama with normalization and batching
/// </summary>
public class OllamaEmbeddingProvider : IEmbeddingProvider, IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly LocalAIOptions _options;
    private readonly ResiliencePipeline _pipeline;
    private readonly ILogger<OllamaEmbeddingProvider> _logger;
    private readonly int _batchSize;

    public int Dimension { get; }
    public string ModelName => _options.EmbeddingModel;
    public string ProviderName => "Ollama";

    public OllamaEmbeddingProvider(
        IHttpClientFactory httpClientFactory,
        IOptions<AIProviderOptions> options,
        ResiliencePipelineProvider<string> pipelineProvider,
        ILogger<OllamaEmbeddingProvider> logger)
    {
        _httpClient = httpClientFactory.CreateClient("Ollama");
        _options = options.Value.Local;
        _pipeline = pipelineProvider.GetPipeline("EmbeddingProvider");
        _logger = logger;
        _batchSize = 16; // Process in smaller batches to avoid timeouts

        // BGE-large-en-v1.5 has 1024 dimensions
        Dimension = _options.EmbeddingModel.Contains("bge-large") ? 1024 
                  : _options.EmbeddingModel.Contains("bge-small") ? 384
                  : 768; // default

        _httpClient.BaseAddress = new Uri(_options.OllamaUrl);
        _httpClient.Timeout = TimeSpan.FromSeconds(60); // Embeddings are usually faster

        _logger.LogInformation("OllamaEmbeddingProvider initialized with model: {Model} ({Dimension}D) at {Url}",
            _options.EmbeddingModel, Dimension, _options.OllamaUrl);
    }

    /// <inheritdoc/>
    public async Task<float[]> GenerateAsync(
        string input,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            _logger.LogWarning("Empty input provided for embedding generation");
            return new float[Dimension];
        }

        try
        {
            _logger.LogDebug("Generating embedding for text ({Length} chars)", input.Length);

            var request = new OllamaEmbedRequest
            {
                Model = _options.EmbeddingModel,
                Input = input
            };

            var response = await _pipeline.ExecuteAsync(
                async ct =>
                {
                    var httpResponse = await _httpClient.PostAsJsonAsync(
                        "/api/embed",
                        request,
                        ct);

                    httpResponse.EnsureSuccessStatusCode();
                    return await httpResponse.Content.ReadFromJsonAsync<OllamaEmbedResponse>(ct)
                        ?? throw new InvalidOperationException("Empty response from Ollama");
                },
                cancellationToken);

            if (response.Embeddings == null || response.Embeddings.Count == 0 || response.Embeddings[0].Length == 0)
            {
                _logger.LogError("Ollama returned empty embedding");
                return new float[Dimension];
            }

            // Validate embedding dimension
            var embeddingDim = response.Embeddings[0].Length;
            if (embeddingDim != Dimension)
            {
                _logger.LogWarning("Ollama returned embedding with dimension {ActualDim}, expected {ExpectedDim}. " +
                    "Check that model is {Model} (should have {ExpectedDim}D). Padding/truncating to match.",
                    embeddingDim, Dimension, _options.EmbeddingModel, Dimension);

                // Pad with zeros if too small, truncate if too large
                var adjusted = new float[Dimension];
                Array.Copy(response.Embeddings[0], adjusted, Math.Min(embeddingDim, Dimension));
                response.Embeddings[0] = adjusted;
            }

            // Normalize the embedding (use first embedding from batch)
            var normalized = Normalize(response.Embeddings[0]);
            
            _logger.LogDebug("Embedding generated successfully ({Dimension}D)", normalized.Length);
            
            return normalized;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "HTTP error calling Ollama embeddings API");
            throw new InvalidOperationException(
                $"Failed to connect to Ollama at {_options.OllamaUrl}. Is Ollama running with the {_options.EmbeddingModel} model?", ex);
        }
        catch (TaskCanceledException ex)
        {
            _logger.LogError(ex, "Embedding request timed out");
            throw new TimeoutException("Embedding generation timed out", ex);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error generating embedding");
            throw;
        }
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<float[]>> GenerateBatchAsync(
        IReadOnlyList<string> inputs,
        CancellationToken cancellationToken = default)
    {
        if (inputs.Count == 0)
            return Array.Empty<float[]>();

        _logger.LogInformation("Generating {Count} embeddings in batches of {BatchSize}", 
            inputs.Count, _batchSize);

        var results = new List<float[]>(inputs.Count);

        // Process in batches to avoid overwhelming Ollama
        for (int i = 0; i < inputs.Count; i += _batchSize)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var batch = inputs.Skip(i).Take(_batchSize).ToList();
            _logger.LogDebug("Processing batch {Current}/{Total}", 
                i / _batchSize + 1, (inputs.Count + _batchSize - 1) / _batchSize);

            // Generate embeddings sequentially within batch (Ollama doesn't have batch API yet)
            var batchTasks = batch.Select(input => GenerateAsync(input, cancellationToken));
            var batchResults = await Task.WhenAll(batchTasks);
            
            results.AddRange(batchResults);
        }

        _logger.LogInformation("Batch embedding generation completed: {Count} embeddings", results.Count);

        return results;
    }

    /// <summary>
    /// Normalizes a vector to unit length (L2 normalization)
    /// </summary>
    private static float[] Normalize(float[] vector)
    {
        var magnitude = Math.Sqrt(vector.Sum(v => v * v));
        
        if (magnitude < 1e-12) // Avoid division by zero
            return vector;

        var normalized = new float[vector.Length];
        for (int i = 0; i < vector.Length; i++)
        {
            normalized[i] = (float)(vector[i] / magnitude);
        }

        return normalized;
    }

    public void Dispose()
    {
        _httpClient?.Dispose();
    }

    #region Ollama API Models

    private class OllamaEmbedRequest
    {
        [JsonPropertyName("model")]
        public required string Model { get; init; }

        [JsonPropertyName("input")]
        public required string Input { get; init; }
    }

    private class OllamaEmbedResponse
    {
        [JsonPropertyName("embeddings")]
        public List<float[]>? Embeddings { get; init; }
    }

    #endregion
}
