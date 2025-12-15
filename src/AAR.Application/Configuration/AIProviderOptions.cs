// =============================================================================
// AAR.Application - Configuration/AIProviderOptions.cs
// Configuration for AI providers (Local/Azure)
// =============================================================================

namespace AAR.Application.Configuration;

/// <summary>
/// Configuration for AI provider selection and settings
/// </summary>
public class AIProviderOptions
{
    /// <summary>
    /// Configuration section name
    /// </summary>
    public const string SectionName = "AI";

    /// <summary>
    /// Provider type: Local or Azure
    /// </summary>
    public string Provider { get; set; } = "Local";

    /// <summary>
    /// Local AI configuration
    /// </summary>
    public LocalAIOptions Local { get; set; } = new();

    /// <summary>
    /// Azure OpenAI configuration
    /// </summary>
    public AzureAIOptions Azure { get; set; } = new();

    /// <summary>
    /// Vector database configuration
    /// </summary>
    public VectorDbOptions VectorDb { get; set; } = new();

    /// <summary>
    /// RAG configuration
    /// </summary>
    public RagOptions Rag { get; set; } = new();
}

/// <summary>
/// Local AI configuration (Ollama + BGE)
/// </summary>
public class LocalAIOptions
{
    /// <summary>
    /// Ollama API base URL
    /// </summary>
    public string OllamaUrl { get; set; } = "http://127.0.0.1:11434";

    /// <summary>
    /// LLM model name
    /// </summary>
    public string LLMModel { get; set; } = "qwen2.5-coder:7b";

    /// <summary>
    /// Embedding model name
    /// </summary>
    public string EmbeddingModel { get; set; } = "bge-large-en-v1.5";

    /// <summary>
    /// LLM request timeout in seconds
    /// </summary>
    public int TimeoutSeconds { get; set; } = 300;

    /// <summary>
    /// Max retries for failed requests
    /// </summary>
    public int MaxRetries { get; set; } = 3;

    /// <summary>
    /// Temperature for LLM responses
    /// </summary>
    public float Temperature { get; set; } = 0.3f;

    /// <summary>
    /// Max tokens for LLM responses
    /// </summary>
    public int MaxTokens { get; set; } = 4096;

    /// <summary>
    /// Timeout strategy configuration for adaptive timeouts
    /// </summary>
    public LLMTimeoutStrategyOptions TimeoutStrategy { get; set; } = new();

    /// <summary>
    /// Enable adaptive timeout calculation based on request parameters
    /// </summary>
    public bool UseAdaptiveTimeout { get; set; } = true;
}

/// <summary>
/// Adaptive timeout strategy configuration for LLM requests
/// </summary>
public class LLMTimeoutStrategyOptions
{
    /// <summary>
    /// Base timeout in seconds for all requests
    /// </summary>
    public int BaseTimeoutSeconds { get; set; } = 60;

    /// <summary>
    /// Per-token timeout in milliseconds (added to base for larger requests)
    /// </summary>
    public double PerTokenTimeoutMs { get; set; } = 10.0;

    /// <summary>
    /// Maximum timeout ceiling in seconds (adaptive never exceeds this)
    /// </summary>
    public int MaxTimeoutSeconds { get; set; } = 600;

    /// <summary>
    /// Minimum timeout floor in seconds
    /// </summary>
    public int MinTimeoutSeconds { get; set; } = 30;

    /// <summary>
    /// Enable connection pooling and keep-alive optimization
    /// </summary>
    public bool EnableConnectionPooling { get; set; } = true;

    /// <summary>
    /// HTTP keep-alive timeout in seconds
    /// </summary>
    public int KeepAliveTimeoutSeconds { get; set; } = 300;

    /// <summary>
    /// Enable graceful degradation on timeout (fallback to cached/heuristic results)
    /// </summary>
    public bool EnableGracefulDegradation { get; set; } = true;

    /// <summary>
    /// Timeout multiplier for streaming vs non-streaming requests
    /// </summary>
    public double StreamingTimeoutMultiplier { get; set; } = 1.5;

    /// <summary>
    /// Timeout multiplier for retry attempts (increases per retry)
    /// </summary>
    public double RetryTimeoutMultiplier { get; set; } = 1.2;
}

/// <summary>
/// Azure OpenAI configuration
/// </summary>
public class AzureAIOptions
{
    /// <summary>
    /// Azure OpenAI endpoint
    /// </summary>
    public string? Endpoint { get; set; }

    /// <summary>
    /// Azure OpenAI API key
    /// </summary>
    public string? ApiKey { get; set; }

    /// <summary>
    /// LLM deployment name
    /// </summary>
    public string LLMDeployment { get; set; } = "gpt-4";

    /// <summary>
    /// Embedding deployment name
    /// </summary>
    public string EmbeddingDeployment { get; set; } = "text-embedding-ada-002";

    /// <summary>
    /// Request timeout in seconds
    /// </summary>
    public int TimeoutSeconds { get; set; } = 120;
}

/// <summary>
/// Vector database configuration
/// </summary>
public class VectorDbOptions
{
    /// <summary>
    /// Vector DB type: Qdrant, InMemory, or Cosmos
    /// </summary>
    public string Type { get; set; } = "Qdrant";

    /// <summary>
    /// Qdrant API URL
    /// </summary>
    public string Url { get; set; } = "http://localhost:6333";

    /// <summary>
    /// API key for Qdrant Cloud (optional)
    /// </summary>
    public string? ApiKey { get; set; }

    /// <summary>
    /// Collection name prefix
    /// </summary>
    public string CollectionPrefix { get; set; } = "aar";

    /// <summary>
    /// Vector dimension (1024 for BGE-large, 1536 for Ada-002)
    /// </summary>
    public int Dimension { get; set; } = 1024;
}

/// <summary>
/// RAG pipeline configuration
/// </summary>
public class RagOptions
{
    /// <summary>
    /// Small file threshold in bytes (sent directly to LLM)
    /// </summary>
    public int SmallFileThresholdBytes { get; set; } = 10240;

    /// <summary>
    /// Chunk size in tokens
    /// </summary>
    public int ChunkSizeTokens { get; set; } = 512;

    /// <summary>
    /// Chunk overlap percentage
    /// </summary>
    public int ChunkOverlapPercent { get; set; } = 20;

    /// <summary>
    /// Top-K chunks to retrieve for context
    /// </summary>
    public int TopK { get; set; } = 10;

    /// <summary>
    /// Minimum similarity score for chunks
    /// </summary>
    public float MinSimilarityScore { get; set; } = 0.7f;

    /// <summary>
    /// Max context tokens to inject into prompt
    /// </summary>
    public int MaxContextTokens { get; set; } = 8000;
}
