// =============================================================================
// AAR.Domain - Enums/ProjectStatus.cs
// Represents the current status of a project's analysis
// =============================================================================

namespace AAR.Domain.Enums;

/// <summary>
/// Status of a project in the analysis pipeline
/// </summary>
public enum ProjectStatus
{
    /// <summary>
    /// Project created, awaiting file upload/clone
    /// </summary>
    Created = 1,
    
    /// <summary>
    /// Files uploaded/cloned successfully
    /// </summary>
    FilesReady = 2,
    
    /// <summary>
    /// Analysis job queued for processing
    /// </summary>
    Queued = 3,
    
    /// <summary>
    /// Analysis in progress
    /// </summary>
    Analyzing = 4,
    
    /// <summary>
    /// Analysis completed successfully
    /// </summary>
    Completed = 5,
    
    /// <summary>
    /// Analysis failed with errors
    /// </summary>
    Failed = 6
}
