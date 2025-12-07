// =============================================================================
// AAR.Application - Interfaces/IAnalysisTelemetry.cs
// Interface for analysis telemetry and metrics
// =============================================================================

namespace AAR.Application.Interfaces;

/// <summary>
/// Interface for tracking analysis metrics and telemetry.
/// </summary>
public interface IAnalysisTelemetry
{
    /// <summary>
    /// Records token consumption for a job.
    /// </summary>
    void RecordTokensConsumed(Guid projectId, int inputTokens, int outputTokens, string modelName);

    /// <summary>
    /// Records an embedding API call.
    /// </summary>
    void RecordEmbeddingCall(Guid projectId, int textCount, int totalTokens, long durationMs);

    /// <summary>
    /// Records retrieval operation metrics.
    /// </summary>
    void RecordRetrieval(Guid projectId, int chunksRetrieved, long durationMs, bool summarized);

    /// <summary>
    /// Records model call metrics.
    /// </summary>
    void RecordModelCall(Guid projectId, string modelName, int inputTokens, int outputTokens, long durationMs);

    /// <summary>
    /// Gets the estimated cost for a job.
    /// </summary>
    CostEstimate EstimateCost(int inputTokens, int outputTokens, string modelName);

    /// <summary>
    /// Gets telemetry summary for a project.
    /// </summary>
    TelemetrySummary GetProjectSummary(Guid projectId);

    /// <summary>
    /// Checks if a job exceeds the maximum allowed cost.
    /// </summary>
    bool ExceedsMaxCost(Guid projectId, decimal maxCost);
}

/// <summary>
/// Cost estimate for an operation.
/// </summary>
public record CostEstimate
{
    /// <summary>
    /// Estimated cost in USD
    /// </summary>
    public required decimal EstimatedCostUsd { get; init; }

    /// <summary>
    /// Input token count
    /// </summary>
    public required int InputTokens { get; init; }

    /// <summary>
    /// Output token count
    /// </summary>
    public required int OutputTokens { get; init; }

    /// <summary>
    /// Model used for estimation
    /// </summary>
    public required string ModelName { get; init; }
}

/// <summary>
/// Summary of telemetry for a project.
/// </summary>
public record TelemetrySummary
{
    /// <summary>
    /// Project ID
    /// </summary>
    public required Guid ProjectId { get; init; }

    /// <summary>
    /// Total tokens consumed (input + output)
    /// </summary>
    public required int TotalTokensConsumed { get; init; }

    /// <summary>
    /// Total embedding API calls
    /// </summary>
    public required int EmbeddingCalls { get; init; }

    /// <summary>
    /// Total retrieval operations
    /// </summary>
    public required int RetrievalOperations { get; init; }

    /// <summary>
    /// Total time spent in retrieval (ms)
    /// </summary>
    public required long TotalRetrievalTimeMs { get; init; }

    /// <summary>
    /// Model calls by model name
    /// </summary>
    public required Dictionary<string, int> ModelCallsByName { get; init; }

    /// <summary>
    /// Estimated total cost in USD
    /// </summary>
    public required decimal EstimatedCostUsd { get; init; }
}
