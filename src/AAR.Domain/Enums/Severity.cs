// =============================================================================
// AAR.Domain - Enums/Severity.cs
// Represents the severity level of a finding
// =============================================================================

namespace AAR.Domain.Enums;

/// <summary>
/// Severity level for code review findings
/// </summary>
public enum Severity
{
    /// <summary>
    /// Informational finding - no action required
    /// </summary>
    Info = 0,
    
    /// <summary>
    /// Low priority issue - cosmetic or minor improvement
    /// </summary>
    Low = 1,
    
    /// <summary>
    /// Medium priority issue - should be addressed
    /// </summary>
    Medium = 2,
    
    /// <summary>
    /// High priority issue - critical problem requiring immediate attention
    /// </summary>
    High = 3,
    
    /// <summary>
    /// Critical issue - security vulnerability or major defect
    /// </summary>
    Critical = 4
}
