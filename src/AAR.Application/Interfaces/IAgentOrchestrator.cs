// =============================================================================
// AAR.Application - Interfaces/IAgentOrchestrator.cs
// Interface for the agent orchestrator that coordinates analysis agents
// =============================================================================

using AAR.Domain.Entities;

namespace AAR.Application.Interfaces;

/// <summary>
/// Interface for the agent orchestrator that coordinates all analysis agents.
/// </summary>
public interface IAgentOrchestrator
{
    /// <summary>
    /// Runs all agents and produces a consolidated report.
    /// </summary>
    /// <param name="projectId">The project ID.</param>
    /// <param name="workingDirectory">The directory containing the project files.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The consolidated report.</returns>
    Task<Report> AnalyzeAsync(Guid projectId, string workingDirectory, CancellationToken cancellationToken = default);
}
