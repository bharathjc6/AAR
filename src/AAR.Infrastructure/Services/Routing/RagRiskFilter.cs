// =============================================================================
// AAR.Infrastructure - Services/Routing/RagRiskFilter.cs
// RAG-based risk filtering to identify high-risk files
// =============================================================================

using AAR.Application.Configuration;
using AAR.Application.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AAR.Infrastructure.Services.Routing;

/// <summary>
/// Uses RAG embeddings to identify high-risk files for priority analysis.
/// </summary>
public class RagRiskFilter : IRagRiskFilter
{
    private readonly IEmbeddingService _embeddingService;
    private readonly IVectorStore _vectorStore;
    private readonly RagProcessingOptions _options;
    private readonly ILogger<RagRiskFilter> _logger;

    public RagRiskFilter(
        IEmbeddingService embeddingService,
        IVectorStore vectorStore,
        IOptions<RagProcessingOptions> options,
        ILogger<RagRiskFilter> logger)
    {
        _embeddingService = embeddingService;
        _vectorStore = vectorStore;
        _options = options.Value;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task<Dictionary<string, float>> ComputeRiskScoresAsync(
        Guid projectId,
        IEnumerable<string> filePaths,
        CancellationToken cancellationToken = default)
    {
        var fileList = filePaths.ToList();
        var riskScores = new Dictionary<string, float>();

        // Initialize all files with zero risk
        foreach (var path in fileList)
        {
            riskScores[path] = 0f;
        }

        if (fileList.Count == 0)
        {
            return riskScores;
        }

        _logger.LogDebug("Computing risk scores for {FileCount} files", fileList.Count);

        try
        {
            // For each risk query, find matching chunks and boost file scores
            foreach (var query in _options.RiskFilterQueries)
            {
                cancellationToken.ThrowIfCancellationRequested();

                // Generate query embedding
                var queryEmbedding = await _embeddingService.CreateEmbeddingAsync(query, cancellationToken);

                // Query vector store for matching chunks
                var results = await _vectorStore.QueryAsync(
                    queryEmbedding,
                    topK: Math.Min(50, fileList.Count), // Limit results
                    projectId,
                    cancellationToken);

                // Aggregate scores by file
                foreach (var result in results)
                {
                    var filePath = result.Metadata.FilePath;
                    if (riskScores.ContainsKey(filePath))
                    {
                        // Add normalized score (higher similarity = higher risk)
                        riskScores[filePath] = Math.Max(riskScores[filePath], result.Score);
                    }
                }
            }

            // Normalize scores to 0-1 range
            var maxScore = riskScores.Values.Max();
            if (maxScore > 0)
            {
                foreach (var key in riskScores.Keys.ToList())
                {
                    riskScores[key] = riskScores[key] / maxScore;
                }
            }

            var highRiskCount = riskScores.Count(kv => kv.Value >= _options.RiskThreshold);
            _logger.LogInformation(
                "Risk scoring complete: {HighRiskCount}/{TotalCount} files above threshold",
                highRiskCount, fileList.Count);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Risk scoring failed, returning zero scores");
        }

        return riskScores;
    }

    /// <inheritdoc/>
    public async Task<List<(string FilePath, float RiskScore)>> GetHighRiskFilesAsync(
        Guid projectId,
        int topK,
        CancellationToken cancellationToken = default)
    {
        var highRiskFiles = new List<(string FilePath, float RiskScore)>();
        var fileScores = new Dictionary<string, float>();

        _logger.LogDebug("Finding top {TopK} high-risk files for project {ProjectId}", topK, projectId);

        try
        {
            foreach (var query in _options.RiskFilterQueries)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var queryEmbedding = await _embeddingService.CreateEmbeddingAsync(query, cancellationToken);

                var results = await _vectorStore.QueryAsync(
                    queryEmbedding,
                    topK: topK * 2, // Get more to ensure we have enough unique files
                    projectId,
                    cancellationToken);

                foreach (var result in results)
                {
                    var filePath = result.Metadata.FilePath;
                    if (fileScores.TryGetValue(filePath, out var existing))
                    {
                        fileScores[filePath] = Math.Max(existing, result.Score);
                    }
                    else
                    {
                        fileScores[filePath] = result.Score;
                    }
                }
            }

            // Get top K by score
            highRiskFiles = fileScores
                .OrderByDescending(kv => kv.Value)
                .Take(topK)
                .Select(kv => (kv.Key, kv.Value))
                .ToList();

            _logger.LogInformation("Found {Count} high-risk files", highRiskFiles.Count);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to get high-risk files");
        }

        return highRiskFiles;
    }
}