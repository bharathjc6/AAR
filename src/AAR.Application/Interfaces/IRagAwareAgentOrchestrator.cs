// =============================================================================
// AAR.Application - Interfaces/IRagAwareAgentOrchestrator.cs
// Interface for RAG-aware agent orchestration
// =============================================================================

using AAR.Application.DTOs;
using AAR.Domain.Entities;

namespace AAR.Application.Interfaces;

/// <summary>
/// Extended agent orchestrator interface supporting RAG-based file routing.
/// </summary>
public interface IRagAwareAgentOrchestrator : IAgentOrchestrator
{
    /// <summary>
    /// Analyzes a project using an analysis plan with routing decisions.
    /// </summary>
    /// <param name="plan">Analysis plan with file routing decisions.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The consolidated report.</returns>
    Task<Report> AnalyzeWithPlanAsync(
        ProjectAnalysisPlan plan,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Analysis context for RAG-aware agents.
/// </summary>
public record RagAnalysisContext
{
    /// <summary>
    /// Project ID.
    /// </summary>
    public Guid ProjectId { get; init; }

    /// <summary>
    /// Working directory.
    /// </summary>
    public required string WorkingDirectory { get; init; }

    /// <summary>
    /// The analysis plan.
    /// </summary>
    public required ProjectAnalysisPlan Plan { get; init; }

    /// <summary>
    /// File currently being analyzed (for progress).
    /// </summary>
    public string? CurrentFile { get; set; }

    /// <summary>
    /// Files processed so far.
    /// </summary>
    public int FilesProcessed { get; set; }
}
