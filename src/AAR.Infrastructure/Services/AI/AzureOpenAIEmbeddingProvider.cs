// =============================================================================
// AAR.Infrastructure - Services/AI/AzureOpenAIEmbeddingProvider.cs
// Azure OpenAI embedding provider (future-ready, currently disabled)
// =============================================================================

using AAR.Application.Configuration;
using AAR.Application.Interfaces;
using Azure;
using Azure.AI.OpenAI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenAI.Embeddings;

namespace AAR.Infrastructure.Services.AI;

/// <summary>
/// Azure OpenAI embedding provider (disabled by default, use Local provider)
/// </summary>
public class AzureOpenAIEmbeddingProvider : IEmbeddingProvider, IDisposable
{
    private readonly AzureOpenAIClient? _client;
    private readonly EmbeddingClient? _embeddingClient;
    private readonly AzureAIOptions _options;
    private readonly ILogger<AzureOpenAIEmbeddingProvider> _logger;
    private readonly bool _isConfigured;

    public int Dimension { get; }
    public string ModelName => _options.EmbeddingDeployment;
    public string ProviderName => "Azure OpenAI";

    public AzureOpenAIEmbeddingProvider(
        IOptions<AIProviderOptions> options,
        ILogger<AzureOpenAIEmbeddingProvider> logger)
    {
        _options = options.Value.Azure;
        _logger = logger;

        // Default to 1536 for Ada-002
        Dimension = 1536;

        _isConfigured = !string.IsNullOrEmpty(_options.Endpoint)
                     && !string.IsNullOrEmpty(_options.ApiKey)
                     && !_options.Endpoint.Contains("TODO")
                     && !_options.ApiKey.Contains("TODO");

        if (_isConfigured)
        {
            try
            {
                _client = new AzureOpenAIClient(
                    new Uri(_options.Endpoint!),
                    new AzureKeyCredential(_options.ApiKey!));

                _embeddingClient = _client.GetEmbeddingClient(_options.EmbeddingDeployment);

                _logger.LogInformation("AzureOpenAIEmbeddingProvider initialized with deployment: {Deployment}",
                    _options.EmbeddingDeployment);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to initialize Azure OpenAI embedding client");
                _isConfigured = false;
            }
        }
        else
        {
            _logger.LogWarning("Azure OpenAI not configured for embeddings. Use Local provider instead.");
        }
    }

    /// <inheritdoc/>
    public async Task<float[]> GenerateAsync(
        string input,
        CancellationToken cancellationToken = default)
    {
        if (!_isConfigured || _embeddingClient == null)
        {
            _logger.LogWarning("Azure OpenAI not configured, returning zero vector");
            return new float[Dimension];
        }

        try
        {
            var response = await _embeddingClient.GenerateEmbeddingAsync(input, cancellationToken: cancellationToken);
            return response.Value.ToFloats().ToArray();
        }
        catch (RequestFailedException ex) when (ex.Status == 429)
        {
            _logger.LogWarning("Azure OpenAI embedding rate limited");
            throw new InvalidOperationException("Rate limited by Azure OpenAI", ex);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating Azure OpenAI embedding");
            throw;
        }
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<float[]>> GenerateBatchAsync(
        IReadOnlyList<string> inputs,
        CancellationToken cancellationToken = default)
    {
        if (!_isConfigured || _embeddingClient == null)
        {
            _logger.LogWarning("Azure OpenAI not configured, returning zero vectors");
            return inputs.Select(_ => new float[Dimension]).ToList();
        }

        if (inputs.Count == 0)
            return Array.Empty<float[]>();

        try
        {
            // Azure OpenAI supports batch embedding
            var response = await _embeddingClient.GenerateEmbeddingsAsync(
                inputs,
                cancellationToken: cancellationToken);

            return response.Value.Select(e => e.ToFloats().ToArray()).ToList();
        }
        catch (RequestFailedException ex) when (ex.Status == 429)
        {
            _logger.LogWarning("Azure OpenAI embedding batch rate limited");
            throw new InvalidOperationException("Rate limited by Azure OpenAI", ex);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating Azure OpenAI embeddings batch");
            throw;
        }
    }

    public void Dispose()
    {
        // Azure SDK doesn't require explicit disposal
    }
}
