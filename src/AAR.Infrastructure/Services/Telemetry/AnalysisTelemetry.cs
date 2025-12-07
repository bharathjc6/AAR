// =============================================================================
// AAR.Infrastructure - Services/Telemetry/AnalysisTelemetry.cs
// Telemetry tracking for analysis operations
// =============================================================================

using System.Collections.Concurrent;
using AAR.Application.Interfaces;
using Microsoft.Extensions.Logging;

namespace AAR.Infrastructure.Services.Telemetry;

/// <summary>
/// In-memory telemetry tracking for analysis operations.
/// </summary>
public class AnalysisTelemetry : IAnalysisTelemetry
{
    private readonly ConcurrentDictionary<Guid, ProjectTelemetry> _projectTelemetry = new();
    private readonly ILogger<AnalysisTelemetry> _logger;

    // Pricing per 1M tokens (approximate as of late 2024)
    private static readonly Dictionary<string, (decimal input, decimal output)> ModelPricing = new()
    {
        ["gpt-4o"] = (2.50m, 10.00m),
        ["gpt-4o-mini"] = (0.15m, 0.60m),
        ["gpt-4"] = (30.00m, 60.00m),
        ["gpt-4-turbo"] = (10.00m, 30.00m),
        ["gpt-3.5-turbo"] = (0.50m, 1.50m),
        ["text-embedding-ada-002"] = (0.10m, 0m),
        ["text-embedding-3-small"] = (0.02m, 0m),
        ["text-embedding-3-large"] = (0.13m, 0m),
        ["mock-embedding"] = (0m, 0m),
        ["mock"] = (0m, 0m)
    };

    public AnalysisTelemetry(ILogger<AnalysisTelemetry> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc/>
    public void RecordTokensConsumed(Guid projectId, int inputTokens, int outputTokens, string modelName)
    {
        var telemetry = GetOrCreateTelemetry(projectId);
        
        lock (telemetry)
        {
            telemetry.TotalInputTokens += inputTokens;
            telemetry.TotalOutputTokens += outputTokens;
            telemetry.ModelCalls.AddOrUpdate(modelName, 1, (_, count) => count + 1);
        }

        _logger.LogDebug(
            "Recorded tokens for {ProjectId}: {Input} input, {Output} output, model: {Model}",
            projectId, inputTokens, outputTokens, modelName);
    }

    /// <inheritdoc/>
    public void RecordEmbeddingCall(Guid projectId, int textCount, int totalTokens, long durationMs)
    {
        var telemetry = GetOrCreateTelemetry(projectId);
        
        lock (telemetry)
        {
            telemetry.EmbeddingCalls++;
            telemetry.EmbeddingTokens += totalTokens;
            telemetry.TotalEmbeddingTimeMs += durationMs;
        }

        _logger.LogDebug(
            "Recorded embedding call for {ProjectId}: {Count} texts, {Tokens} tokens, {Duration}ms",
            projectId, textCount, totalTokens, durationMs);
    }

    /// <inheritdoc/>
    public void RecordRetrieval(Guid projectId, int chunksRetrieved, long durationMs, bool summarized)
    {
        var telemetry = GetOrCreateTelemetry(projectId);
        
        lock (telemetry)
        {
            telemetry.RetrievalOperations++;
            telemetry.TotalChunksRetrieved += chunksRetrieved;
            telemetry.TotalRetrievalTimeMs += durationMs;
            if (summarized) telemetry.SummarizationCount++;
        }

        _logger.LogDebug(
            "Recorded retrieval for {ProjectId}: {Chunks} chunks, {Duration}ms, summarized: {Summarized}",
            projectId, chunksRetrieved, durationMs, summarized);
    }

    /// <inheritdoc/>
    public void RecordModelCall(Guid projectId, string modelName, int inputTokens, int outputTokens, long durationMs)
    {
        RecordTokensConsumed(projectId, inputTokens, outputTokens, modelName);
        
        var telemetry = GetOrCreateTelemetry(projectId);
        lock (telemetry)
        {
            telemetry.TotalModelCallTimeMs += durationMs;
        }
    }

    /// <inheritdoc/>
    public CostEstimate EstimateCost(int inputTokens, int outputTokens, string modelName)
    {
        var pricing = ModelPricing.GetValueOrDefault(modelName.ToLowerInvariant(), (input: 0m, output: 0m));
        
        var inputCost = (inputTokens / 1_000_000m) * pricing.input;
        var outputCost = (outputTokens / 1_000_000m) * pricing.output;

        return new CostEstimate
        {
            EstimatedCostUsd = inputCost + outputCost,
            InputTokens = inputTokens,
            OutputTokens = outputTokens,
            ModelName = modelName
        };
    }

    /// <inheritdoc/>
    public TelemetrySummary GetProjectSummary(Guid projectId)
    {
        var telemetry = GetOrCreateTelemetry(projectId);

        decimal estimatedCost;
        Dictionary<string, int> modelCalls;
        
        lock (telemetry)
        {
            modelCalls = new Dictionary<string, int>(telemetry.ModelCalls);
            
            // Calculate total cost
            estimatedCost = 0m;
            foreach (var (model, count) in modelCalls)
            {
                // Estimate average tokens per call
                var avgInputTokens = telemetry.TotalInputTokens / Math.Max(1, modelCalls.Values.Sum());
                var avgOutputTokens = telemetry.TotalOutputTokens / Math.Max(1, modelCalls.Values.Sum());
                var estimate = EstimateCost(avgInputTokens * count, avgOutputTokens * count, model);
                estimatedCost += estimate.EstimatedCostUsd;
            }

            // Add embedding costs
            var embeddingCost = EstimateCost(telemetry.EmbeddingTokens, 0, "text-embedding-ada-002");
            estimatedCost += embeddingCost.EstimatedCostUsd;
        }

        return new TelemetrySummary
        {
            ProjectId = projectId,
            TotalTokensConsumed = telemetry.TotalInputTokens + telemetry.TotalOutputTokens,
            EmbeddingCalls = telemetry.EmbeddingCalls,
            RetrievalOperations = telemetry.RetrievalOperations,
            TotalRetrievalTimeMs = telemetry.TotalRetrievalTimeMs,
            ModelCallsByName = modelCalls,
            EstimatedCostUsd = estimatedCost
        };
    }

    /// <inheritdoc/>
    public bool ExceedsMaxCost(Guid projectId, decimal maxCost)
    {
        var summary = GetProjectSummary(projectId);
        return summary.EstimatedCostUsd > maxCost;
    }

    private ProjectTelemetry GetOrCreateTelemetry(Guid projectId)
    {
        return _projectTelemetry.GetOrAdd(projectId, _ => new ProjectTelemetry());
    }

    private class ProjectTelemetry
    {
        public int TotalInputTokens { get; set; }
        public int TotalOutputTokens { get; set; }
        public int EmbeddingCalls { get; set; }
        public int EmbeddingTokens { get; set; }
        public long TotalEmbeddingTimeMs { get; set; }
        public int RetrievalOperations { get; set; }
        public int TotalChunksRetrieved { get; set; }
        public long TotalRetrievalTimeMs { get; set; }
        public long TotalModelCallTimeMs { get; set; }
        public int SummarizationCount { get; set; }
        public ConcurrentDictionary<string, int> ModelCalls { get; } = new();
    }
}
