// =============================================================================
// AAR.Infrastructure - Services/Embedding/AzureOpenAiEmbeddingService.cs
// Azure OpenAI embedding implementation
// =============================================================================

using AAR.Application.Interfaces;
using Azure;
using Azure.AI.OpenAI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenAI.Embeddings;

namespace AAR.Infrastructure.Services.Embedding;

/// <summary>
/// Azure OpenAI embedding service implementation.
/// </summary>
public class AzureOpenAiEmbeddingService : IEmbeddingService
{
    private readonly EmbeddingOptions _options;
    private readonly ILogger<AzureOpenAiEmbeddingService> _logger;
    private readonly EmbeddingClient? _client;

    /// <inheritdoc/>
    public string ModelName => _options.Model;

    /// <inheritdoc/>
    public int Dimension => _options.Dimension;

    /// <inheritdoc/>
    public bool IsMock => false;

    public AzureOpenAiEmbeddingService(
        IOptions<EmbeddingOptions> options,
        ILogger<AzureOpenAiEmbeddingService> logger)
    {
        _options = options.Value;
        _logger = logger;

        var endpoint = _options.Endpoint ?? Environment.GetEnvironmentVariable("AZURE_OPENAI_ENDPOINT");
        var apiKey = _options.ApiKey ?? Environment.GetEnvironmentVariable("AZURE_OPENAI_KEY");

        if (!string.IsNullOrEmpty(endpoint) && !string.IsNullOrEmpty(apiKey) &&
            !endpoint.Contains("TODO") && !apiKey.Contains("TODO"))
        {
            try
            {
                var azureClient = new AzureOpenAIClient(new Uri(endpoint), new AzureKeyCredential(apiKey));
                _client = azureClient.GetEmbeddingClient(_options.Model);
                _logger.LogInformation("AzureOpenAiEmbeddingService initialized with model: {Model}", _options.Model);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to initialize Azure OpenAI embedding client");
                throw;
            }
        }
        else
        {
            _logger.LogWarning("Azure OpenAI embedding credentials not configured");
            throw new InvalidOperationException("Azure OpenAI credentials not configured for embedding service");
        }
    }

    /// <inheritdoc/>
    public async Task<float[]> CreateEmbeddingAsync(string text, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(text))
            return new float[_options.Dimension];

        try
        {
            var response = await _client!.GenerateEmbeddingAsync(text, cancellationToken: cancellationToken);
            return response.Value.ToFloats().ToArray();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating embedding");
            throw;
        }
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<float[]>> CreateEmbeddingsAsync(
        IEnumerable<string> texts, 
        CancellationToken cancellationToken = default)
    {
        var textList = texts.ToList();
        
        if (textList.Count == 0)
            return [];

        var allEmbeddings = new List<float[]>();

        // Process in batches
        for (var i = 0; i < textList.Count; i += _options.BatchSize)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var batch = textList.Skip(i).Take(_options.BatchSize).ToList();
            
            try
            {
                var response = await _client!.GenerateEmbeddingsAsync(batch, cancellationToken: cancellationToken);
                
                foreach (var embedding in response.Value)
                {
                    allEmbeddings.Add(embedding.ToFloats().ToArray());
                }

                _logger.LogDebug("Generated {Count} embeddings in batch {BatchNum}", 
                    batch.Count, (i / _options.BatchSize) + 1);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error generating embeddings for batch starting at index {Index}", i);
                throw;
            }
        }

        return allEmbeddings;
    }
}
