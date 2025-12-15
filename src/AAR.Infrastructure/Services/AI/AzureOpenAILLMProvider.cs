// =============================================================================
// AAR.Infrastructure - Services/AI/AzureOpenAILLMProvider.cs
// Azure OpenAI LLM provider (future-ready, currently disabled)
// =============================================================================

using System.Diagnostics;
using AAR.Application.Configuration;
using AAR.Application.Interfaces;
using AAR.Shared;
using Azure;
using Azure.AI.OpenAI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenAI.Chat;

namespace AAR.Infrastructure.Services.AI;

/// <summary>
/// Azure OpenAI LLM provider (disabled by default, use Local provider)
/// </summary>
public class AzureOpenAILLMProvider : ILLMProvider, IDisposable
{
    private readonly AzureOpenAIClient? _client;
    private readonly ChatClient? _chatClient;
    private readonly AzureAIOptions _options;
    private readonly ILogger<AzureOpenAILLMProvider> _logger;
    private readonly bool _isConfigured;

    public string ProviderName => "Azure OpenAI";
    public string ModelName => _options.LLMDeployment;

    public AzureOpenAILLMProvider(
        IOptions<AIProviderOptions> options,
        ILogger<AzureOpenAILLMProvider> logger)
    {
        _options = options.Value.Azure;
        _logger = logger;

        // Check if Azure OpenAI is configured
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

                _chatClient = _client.GetChatClient(_options.LLMDeployment);

                _logger.LogInformation("AzureOpenAILLMProvider initialized with deployment: {Deployment}",
                    _options.LLMDeployment);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to initialize Azure OpenAI client");
                _isConfigured = false;
            }
        }
        else
        {
            _logger.LogWarning("Azure OpenAI not configured. Use Local provider instead.");
        }
    }

    /// <inheritdoc/>
    public async Task<Result<LLMResponse>> AnalyzeAsync(
        LLMRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!_isConfigured || _chatClient == null)
        {
            return Result<LLMResponse>.Failure(new Error(
                "Azure.NotConfigured",
                "Azure OpenAI is not configured. Use Local provider."));
        }

        var stopwatch = Stopwatch.StartNew();

        try
        {
            var messages = new List<ChatMessage>
            {
                new SystemChatMessage(request.SystemPrompt),
                new UserChatMessage(request.UserPrompt)
            };

            var chatOptions = new ChatCompletionOptions
            {
                MaxOutputTokenCount = request.MaxTokens,
                Temperature = request.Temperature
            };

            var response = await _chatClient.CompleteChatAsync(messages, chatOptions, cancellationToken);

            stopwatch.Stop();

            return Result<LLMResponse>.Success(new LLMResponse
            {
                Content = response.Value.Content[0].Text,
                PromptTokens = response.Value.Usage.InputTokenCount,
                CompletionTokens = response.Value.Usage.OutputTokenCount,
                Model = response.Value.Model,
                Duration = stopwatch.Elapsed,
                FinishReason = response.Value.FinishReason.ToString()
            });
        }
        catch (RequestFailedException ex) when (ex.Status == 429)
        {
            _logger.LogWarning("Azure OpenAI rate limited");
            return Result<LLMResponse>.Failure(new Error(
                "Azure.RateLimited",
                "Rate limited by Azure OpenAI"));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error calling Azure OpenAI");
            return Result<LLMResponse>.Failure(new Error(
                "Azure.Error",
                $"Azure OpenAI error: {ex.Message}"));
        }
    }

    /// <inheritdoc/>
    public Task<Result<LLMResponse>> AnalyzeStreamingAsync(
        LLMRequest request,
        Action<string> onChunk,
        CancellationToken cancellationToken = default)
    {
        // Streaming implementation for Azure OpenAI
        // Similar to Ollama but using Azure SDK streaming
        throw new NotImplementedException("Azure OpenAI streaming not yet implemented");
    }

    /// <inheritdoc/>
    public Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(_isConfigured && _chatClient != null);
    }

    public void Dispose()
    {
        // Azure SDK doesn't require explicit disposal
    }
}
