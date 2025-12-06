// =============================================================================
// AAR.Application - Interfaces/IOpenAiService.cs
// Abstraction for OpenAI/Azure OpenAI operations
// =============================================================================

using AAR.Application.DTOs;
using AAR.Domain.Enums;
using AAR.Shared;

namespace AAR.Application.Interfaces;

/// <summary>
/// Interface for OpenAI/Azure OpenAI operations
/// Implementations can use real Azure OpenAI or mock responses
/// </summary>
public interface IOpenAiService
{
    /// <summary>
    /// Analyzes code using a specific agent type
    /// </summary>
    /// <param name="agentType">Type of analysis agent to use</param>
    /// <param name="context">Analysis context with project info</param>
    /// <param name="fileContents">Dictionary of file paths to contents</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Analysis response from the agent</returns>
    Task<Result<AgentAnalysisResponse>> AnalyzeAsync(
        AgentType agentType,
        AnalysisContext context,
        IDictionary<string, string> fileContents,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Analyzes code using a prompt string (simple interface for agents)
    /// </summary>
    /// <param name="prompt">The analysis prompt</param>
    /// <param name="agentType">Type of analysis agent</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>JSON response from the AI</returns>
    Task<string> AnalyzeCodeAsync(string prompt, string agentType, CancellationToken cancellationToken = default);

    /// <summary>
    /// Generates a summary of multiple agent analyses
    /// </summary>
    Task<Result<string>> GenerateSummaryAsync(
        AnalysisContext context,
        IEnumerable<AgentAnalysisResponse> agentResponses,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Generates fix suggestions/patches
    /// </summary>
    Task<Result<string>> GeneratePatchAsync(
        string filePath,
        string originalCode,
        string issue,
        string suggestedFix,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks if the service is configured and available
    /// </summary>
    Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets whether the service is running in mock mode
    /// </summary>
    bool IsMockMode { get; }
}
