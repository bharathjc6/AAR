// =============================================================================
// AAR.Application - Services/ClusterSynthesizer.cs
// Cluster-level synthesis: one LLM call per cluster to produce consolidated findings
// =============================================================================

using AAR.Application.DTOs;
using AAR.Application.Interfaces;
using Microsoft.Extensions.Logging;

namespace AAR.Application.Services;

public class ClusterSynthesizer
{
    private readonly IOpenAiService _openAiService;
    private readonly ILogger<ClusterSynthesizer> _logger;

    public ClusterSynthesizer(IOpenAiService openAiService, ILogger<ClusterSynthesizer> logger)
    {
        _openAiService = openAiService;
        _logger = logger;
    }

    public async Task<List<AgentFinding>> SynthesizeAsync(IEnumerable<AgentFinding> findings, CancellationToken cancellationToken = default)
    {
        // Group findings by a lightweight fingerprint: symbol|file|category
        var groups = findings.GroupBy(f => BuildFingerprintKey(f));
        var result = new List<AgentFinding>();

        foreach (var g in groups)
        {
            var groupFindings = g.ToList();

            // Prepare a single AgentAnalysisResponse representing this cluster
            var clusterResponse = new AgentAnalysisResponse
            {
                Findings = groupFindings,
                Summary = string.Empty,
                Recommendations = new List<string>()
            };

            try
            {
                // Create a minimal AnalysisContext - LLM prompts can be customized in service implementation
                var context = new AnalysisContext { ProjectId = Guid.Empty, ProjectName = string.Empty };
                var llmResult = await _openAiService.GenerateSummaryAsync(context, new[] { clusterResponse }, cancellationToken);

                string? clusterSummary = null;
                if (llmResult.IsSuccess && !string.IsNullOrWhiteSpace(llmResult.Value))
                {
                    // If LLM returned JSON, try to extract a human-friendly "summary" field
                    try
                    {
                        using var doc = System.Text.Json.JsonDocument.Parse(llmResult.Value);
                        var root = doc.RootElement;
                        if (root.ValueKind == System.Text.Json.JsonValueKind.Object && root.TryGetProperty("summary", out var s) && s.ValueKind == System.Text.Json.JsonValueKind.String)
                        {
                            clusterSummary = s.GetString();
                        }
                        else if (root.ValueKind == System.Text.Json.JsonValueKind.Object && root.TryGetProperty("summaryText", out var s2) && s2.ValueKind == System.Text.Json.JsonValueKind.String)
                        {
                            clusterSummary = s2.GetString();
                        }
                        else
                        {
                            // Not JSON with a summary field - fallback to raw text but strip surrounding braces if present
                            clusterSummary = llmResult.Value.Trim();
                            if (clusterSummary.StartsWith("{") && clusterSummary.EndsWith("}"))
                            {
                                // Try to remove top-level JSON if it's not useful
                                clusterSummary = string.Empty;
                            }
                        }
                    }
                    catch (System.Text.Json.JsonException)
                    {
                        clusterSummary = llmResult.Value.Trim();
                    }
                }

                // Build a consolidated finding
                var consolidated = ConsolidateGroup(groupFindings, clusterSummary);
                result.Add(consolidated);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Cluster synthesis failed for key {Key}; falling back to merge", g.Key);
                result.Add(ConsolidateGroup(groupFindings, null));
            }
        }

        return result;
    }

    private static string BuildFingerprintKey(AgentFinding f)
        => string.Join("|", new[] { f.Symbol ?? string.Empty, f.FilePath ?? string.Empty, f.Category ?? string.Empty });

    private static AgentFinding ConsolidateGroup(List<AgentFinding> groupFindings, string? clusterSummary)
    {
        // Highest severity mapping: High > Medium > Low
        var severity = groupFindings.Select(g => g.Severity).FirstOrDefault(s => string.Equals(s, "High", StringComparison.OrdinalIgnoreCase))
            ?? groupFindings.Select(g => g.Severity).FirstOrDefault(s => string.Equals(s, "Medium", StringComparison.OrdinalIgnoreCase))
            ?? groupFindings.Select(g => g.Severity).FirstOrDefault() ?? "Medium";

        var maxConfidence = groupFindings.Max(g => g.Confidence);

        var filePath = groupFindings.Select(g => g.FilePath).Distinct().Count() == 1
            ? groupFindings.First().FilePath
            : null;

        var symbol = groupFindings.Select(g => g.Symbol).Distinct().Count() == 1
            ? groupFindings.First().Symbol
            : null;

        var category = groupFindings.Select(g => g.Category).FirstOrDefault() ?? string.Empty;

        var desc = string.Join("\n---\n", groupFindings.Select(g => g.Description).Where(s => !string.IsNullOrWhiteSpace(s)));
        var explanation = !string.IsNullOrWhiteSpace(clusterSummary) ? clusterSummary : string.Join("\n\n", groupFindings.Select(g => g.Explanation).Where(s => !string.IsNullOrWhiteSpace(s)));

        return new AgentFinding
        {
            Id = Guid.NewGuid().ToString(),
            FilePath = filePath,
            LineRange = null,
            Category = category,
            Severity = severity,
            Description = desc,
            Explanation = explanation ?? string.Empty,
            SuggestedFix = groupFindings.Select(g => g.SuggestedFix).FirstOrDefault(s => !string.IsNullOrWhiteSpace(s)),
            FixedCodeSnippet = null,
            OriginalCodeSnippet = null,
            Symbol = symbol,
            Confidence = maxConfidence
        };
    }
}
