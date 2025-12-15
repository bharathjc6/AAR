// =============================================================================
// AAR.Application - Configuration/AgentGuardrailOptions.cs
// Configuration for per-agent guardrails (allowed categories, confidence, limits)
// =============================================================================

using AAR.Domain.Enums;
using System.Collections.Generic;

namespace AAR.Application.Configuration;

public sealed class AgentLimits
{
    public List<FindingCategory> AllowedCategories { get; set; } = new List<FindingCategory>();
    public double MinConfidence { get; set; } = 0.0;
    public int MaxFindings { get; set; } = 100;
    public int MaxRagInjections { get; set; } = 50;
}

public sealed class AgentGuardrailOptions
{
    public const string SectionName = "AgentGuardrails";

    /// <summary>
    /// Per-agent limits keyed by agent name (e.g., "CodeQuality", "Security").
    /// </summary>
    public Dictionary<string, AgentLimits> AgentLimits { get; set; } = new Dictionary<string, AgentLimits>();
}
