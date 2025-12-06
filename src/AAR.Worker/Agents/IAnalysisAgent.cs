using AAR.Domain.Entities;
using AAR.Domain.Enums;

namespace AAR.Worker.Agents;

/// <summary>
/// Interface for analysis agents.
/// </summary>
public interface IAnalysisAgent
{
    /// <summary>
    /// Gets the type of agent.
    /// </summary>
    AgentType AgentType { get; }

    /// <summary>
    /// Analyzes the project files and returns findings.
    /// </summary>
    /// <param name="projectId">The project ID.</param>
    /// <param name="workingDirectory">The directory containing the project files.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A list of review findings.</returns>
    Task<List<ReviewFinding>> AnalyzeAsync(Guid projectId, string workingDirectory, CancellationToken cancellationToken = default);
}
