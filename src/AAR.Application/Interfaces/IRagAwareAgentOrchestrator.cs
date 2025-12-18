using System;
using System.Threading;
using System.Threading.Tasks;
using AAR.Application.DTOs;
using AAR.Domain.Entities;

namespace AAR.Application.Interfaces;

/// <summary>
/// Orchestrator interface for RAG-aware analysis agents.
/// </summary>
public interface IRagAwareAgentOrchestrator
{
    /// <summary>
    /// Run a basic analysis for a project (no routing plan).
    /// </summary>
    Task<Report> AnalyzeAsync(
        Guid projectId,
        string workingDirectory,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Run analysis using a precomputed routing plan.
    /// </summary>
    Task<Report> AnalyzeWithPlanAsync(
        ProjectAnalysisPlan plan,
        CancellationToken cancellationToken = default);
}
