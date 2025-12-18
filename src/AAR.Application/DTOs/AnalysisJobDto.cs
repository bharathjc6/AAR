// =============================================================================
// AAR.Application - DTOs/AnalysisJobDto.cs
// Data Transfer Objects for analysis job queue
// =============================================================================

namespace AAR.Application.DTOs;

/// <summary>
/// Message for the analysis job queue
/// </summary>
public record AnalysisJobMessage
{
    /// <summary>
    /// Project ID to analyze
    /// </summary>
    public Guid ProjectId { get; init; }
    
    /// <summary>
    /// When the job was enqueued
    /// </summary>
    public DateTime EnqueuedAt { get; init; } = DateTime.UtcNow;
    
    /// <summary>
    /// Priority level (higher = more urgent)
    /// </summary>
    public int Priority { get; init; } = 0;
    
    /// <summary>
    /// Number of retry attempts
    /// </summary>
    public int RetryCount { get; init; } = 0;
}

/// <summary>
/// Context passed to analysis agents
/// </summary>
public record AnalysisContext
{
    /// <summary>
    /// Project being analyzed
    /// </summary>
    public Guid ProjectId { get; init; }
    
    /// <summary>
    /// Project name
    /// </summary>
    public string ProjectName { get; init; } = string.Empty;
    
    /// <summary>
    /// Temporary directory containing extracted files
    /// </summary>
    public string WorkingDirectory { get; init; } = string.Empty;
    
    /// <summary>
    /// List of files to analyze
    /// </summary>
    public IReadOnlyList<AnalysisFileInfo> Files { get; init; } = Array.Empty<AnalysisFileInfo>();
}

/// <summary>
/// File information for analysis
/// </summary>
public record AnalysisFileInfo
{
    /// <summary>
    /// Relative path within the project
    /// </summary>
    public string RelativePath { get; init; } = string.Empty;
    
    /// <summary>
    /// Absolute path on disk
    /// </summary>
    public string AbsolutePath { get; init; } = string.Empty;
    
    /// <summary>
    /// File extension
    /// </summary>
    public string Extension { get; init; } = string.Empty;
    
    /// <summary>
    /// File size in bytes
    /// </summary>
    public long Size { get; init; }
    
    /// <summary>
    /// File content (loaded on demand)
    /// </summary>
    public string? Content { get; set; }
}
