// =============================================================================
// AAR.Domain - Enums/FindingCategory.cs
// Categories of findings from the analysis agents
// =============================================================================

namespace AAR.Domain.Enums;

/// <summary>
/// Category of a code review finding
/// </summary>
public enum FindingCategory
{
    /// <summary>
    /// Performance-related issues (slow code, inefficient algorithms)
    /// </summary>
    Performance = 1,
    
    /// <summary>
    /// Security vulnerabilities and risks
    /// </summary>
    Security = 2,
    
    /// <summary>
    /// Architectural concerns (layering, dependencies, patterns)
    /// </summary>
    Architecture = 3,
    
    /// <summary>
    /// Code quality issues (readability, maintainability, conventions)
    /// </summary>
    CodeQuality = 4,
    
    /// <summary>
    /// Project structure concerns (file organization, naming)
    /// </summary>
    Structure = 5,
    
    /// <summary>
    /// Code complexity issues
    /// </summary>
    Complexity = 6,
    
    /// <summary>
    /// Maintainability concerns
    /// </summary>
    Maintainability = 7,
    
    /// <summary>
    /// Best practices and recommendations
    /// </summary>
    BestPractice = 8,
    
    /// <summary>
    /// Other/miscellaneous issues
    /// </summary>
    Other = 99
}
