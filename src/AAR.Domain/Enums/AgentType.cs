// =============================================================================
// AAR.Domain - Enums/AgentType.cs
// Types of analysis agents in the system
// =============================================================================

namespace AAR.Domain.Enums;

/// <summary>
/// Type of analysis agent
/// </summary>
public enum AgentType
{
    /// <summary>
    /// Analyzes project structure and file organization
    /// </summary>
    Structure = 1,
    
    /// <summary>
    /// Analyzes code quality (readability, maintainability, conventions)
    /// </summary>
    CodeQuality = 2,
    
    /// <summary>
    /// Analyzes security vulnerabilities and risks
    /// </summary>
    Security = 3,
    
    /// <summary>
    /// Provides architectural guidance and pattern recommendations
    /// </summary>
    ArchitectureAdvisor = 4
}
