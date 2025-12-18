// =============================================================================
// AAR.Application - Services/CrossFileSynthesizer.cs
// Synthesizes cross-file narratives and attack/impact paths from consolidated findings
// =============================================================================

using AAR.Application.DTOs;
using AAR.Application.Interfaces;
using Microsoft.Extensions.Logging;

namespace AAR.Application.Services;

public class CrossFileSynthesizer
{
    private readonly IOpenAiService _openAiService;
    private readonly ILogger<CrossFileSynthesizer> _logger;

    public CrossFileSynthesizer(IOpenAiService openAiService, ILogger<CrossFileSynthesizer> logger)
    {
        _openAiService = openAiService;
        _logger = logger;
    }

    public async Task<string> SynthesizeNarrativeAsync(IEnumerable<AgentFinding> findings, CancellationToken cancellationToken = default)
    {
        try
        {
            var context = new AnalysisContext { ProjectId = Guid.Empty, ProjectName = string.Empty };
            var response = await _openAiService.GenerateSummaryAsync(context, new[] { new AgentAnalysisResponse { Findings = findings.ToList() } }, cancellationToken);
            if (response.IsSuccess && !string.IsNullOrWhiteSpace(response.Value))
            {
                try
                {
                    using var doc = System.Text.Json.JsonDocument.Parse(response.Value);
                    var root = doc.RootElement;
                    if (root.ValueKind == System.Text.Json.JsonValueKind.Object && root.TryGetProperty("summary", out var s) && s.ValueKind == System.Text.Json.JsonValueKind.String)
                    {
                        return s.GetString() ?? string.Empty;
                    }
                    if (root.ValueKind == System.Text.Json.JsonValueKind.Object && root.TryGetProperty("narrative", out var n) && n.ValueKind == System.Text.Json.JsonValueKind.String)
                    {
                        return n.GetString() ?? string.Empty;
                    }
                }
                catch (System.Text.Json.JsonException)
                {
                    // Not JSON - return raw text
                    return response.Value;
                }

                // If JSON but no recognized field, avoid embedding raw JSON into UI
                var trimmed = response.Value.Trim();
                if (!string.IsNullOrWhiteSpace(trimmed) && !trimmed.StartsWith("{"))
                    return trimmed;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Cross-file synthesis failed");
        }

        // Fallback: simple stitched narrative
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("Cross-file findings narrative:");
        foreach (var f in findings.Take(20))
        {
            sb.AppendLine($"- {f.Category} ({f.Severity}) in {(f.FilePath ?? f.Symbol ?? "<unknown>")}: {f.Description?.Split('\n').FirstOrDefault()}");
        }

        return sb.ToString();
    }
}
