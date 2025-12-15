// =============================================================================
// AAR.Application - Interfaces/ILLMProvider.cs
// Provider abstraction for LLM inference (Ollama/Azure OpenAI)
// =============================================================================

using AAR.Shared;

namespace AAR.Application.Interfaces;

/// <summary>
/// Provider abstraction for LLM inference operations
/// </summary>
public interface ILLMProvider
{
    /// <summary>
    /// Analyzes code using the LLM
    /// </summary>
    /// <param name="request">LLM request with system/user prompts</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>LLM response</returns>
    Task<Result<LLMResponse>> AnalyzeAsync(
        LLMRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Analyzes code with streaming response
    /// </summary>
    /// <param name="request">LLM request with system/user prompts</param>
    /// <param name="onChunk">Callback for each response chunk</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Complete LLM response</returns>
    Task<Result<LLMResponse>> AnalyzeStreamingAsync(
        LLMRequest request,
        Action<string> onChunk,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks if the provider is available
    /// </summary>
    Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the provider name
    /// </summary>
    string ProviderName { get; }

    /// <summary>
    /// Gets the model name being used
    /// </summary>
    string ModelName { get; }
}

/// <summary>
/// LLM request
/// </summary>
public record LLMRequest
{
    /// <summary>
    /// System prompt (sets the behavior/role)
    /// </summary>
    public required string SystemPrompt { get; init; }

    /// <summary>
    /// User prompt (the actual task/question)
    /// </summary>
    public required string UserPrompt { get; init; }

    /// <summary>
    /// Temperature (0.0 = deterministic, 1.0 = creative)
    /// </summary>
    public float Temperature { get; init; } = 0.3f;

    /// <summary>
    /// Maximum tokens in response
    /// </summary>
    public int MaxTokens { get; init; } = 4096;

    /// <summary>
    /// Additional context data
    /// </summary>
    public Dictionary<string, object>? Metadata { get; init; }
}

/// <summary>
/// LLM response
/// </summary>
public record LLMResponse
{
    /// <summary>
    /// Generated text
    /// </summary>
    public required string Content { get; init; }

    /// <summary>
    /// Tokens used in prompt
    /// </summary>
    public int PromptTokens { get; init; }

    /// <summary>
    /// Tokens generated in response
    /// </summary>
    public int CompletionTokens { get; init; }

    /// <summary>
    /// Total tokens
    /// </summary>
    public int TotalTokens => PromptTokens + CompletionTokens;

    /// <summary>
    /// Model used
    /// </summary>
    public string? Model { get; init; }

    /// <summary>
    /// Response generation time
    /// </summary>
    public TimeSpan Duration { get; init; }

    /// <summary>
    /// Finish reason (completed, length, error)
    /// </summary>
    public string? FinishReason { get; init; }
}
