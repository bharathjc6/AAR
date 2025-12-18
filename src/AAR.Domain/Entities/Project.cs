// =============================================================================
// AAR.Domain - Entities/Project.cs
// Represents a project submitted for architecture review
// =============================================================================

using AAR.Domain.Enums;

namespace AAR.Domain.Entities;

/// <summary>
/// Represents a code repository project submitted for analysis
/// </summary>
public class Project : BaseEntity
{
    /// <summary>
    /// User-provided name for the project
    /// </summary>
    public string Name { get; private set; } = string.Empty;
    
    /// <summary>
    /// Optional description of the project
    /// </summary>
    public string? Description { get; private set; }
    
    /// <summary>
    /// Git repository URL (if cloned from remote)
    /// </summary>
    public string? GitRepoUrl { get; private set; }
    
    /// <summary>
    /// Original filename if uploaded as a zip
    /// </summary>
    public string? OriginalFileName { get; private set; }
    
    /// <summary>
    /// Blob storage path where project files are stored
    /// </summary>
    public string? StoragePath { get; private set; }
    
    /// <summary>
    /// Current status of the project analysis
    /// </summary>
    public ProjectStatus Status { get; private set; } = ProjectStatus.Created;
    
    /// <summary>
    /// Error message if the analysis failed
    /// </summary>
    public string? ErrorMessage { get; private set; }
    
    /// <summary>
    /// When analysis started
    /// </summary>
    public DateTime? AnalysisStartedAt { get; private set; }
    
    /// <summary>
    /// When analysis completed
    /// </summary>
    public DateTime? AnalysisCompletedAt { get; private set; }
    
    /// <summary>
    /// Total file count in the project
    /// </summary>
    public int FileCount { get; private set; }
    
    /// <summary>
    /// Total lines of code
    /// </summary>
    public int TotalLinesOfCode { get; private set; }
    
    /// <summary>
    /// ID of the API key that created this project
    /// </summary>
    public Guid? ApiKeyId { get; private set; }

    /// <summary>
    /// Navigation property to file records
    /// </summary>
    public ICollection<FileRecord> Files { get; private set; } = new List<FileRecord>();
    
    /// <summary>
    /// Navigation property to the report
    /// </summary>
    public Report? Report { get; private set; }

    // Private constructor for EF Core
    private Project() { }

    /// <summary>
    /// Creates a new project from a zip file upload
    /// </summary>
    public static Project CreateFromZipUpload(string name, string originalFileName, string? description = null)
    {
        return new Project
        {
            Name = name,
            OriginalFileName = originalFileName,
            Description = description,
            Status = ProjectStatus.Created
        };
    }

    /// <summary>
    /// Creates a new project from a Git repository URL
    /// </summary>
    public static Project CreateFromGitRepo(string name, string gitRepoUrl, string? description = null)
    {
        if (string.IsNullOrWhiteSpace(gitRepoUrl))
            throw new ArgumentException("Git repository URL is required", nameof(gitRepoUrl));

        return new Project
        {
            Name = name,
            GitRepoUrl = gitRepoUrl,
            Description = description,
            Status = ProjectStatus.Created
        };
    }

    /// <summary>
    /// Sets the storage path after files are uploaded/extracted
    /// </summary>
    public void SetStoragePath(string storagePath)
    {
        StoragePath = storagePath;
        Status = ProjectStatus.FilesReady;
        SetUpdated();
    }

    /// <summary>
    /// Marks the project as queued for analysis
    /// </summary>
    public void MarkAsQueued()
    {
        if (Status == ProjectStatus.Analyzing)
            throw new InvalidOperationException("Project is already being analyzed");
            
        Status = ProjectStatus.Queued;
        SetUpdated();
    }

    /// <summary>
    /// Marks the project as currently being analyzed
    /// </summary>
    public void StartAnalysis()
    {
        Status = ProjectStatus.Analyzing;
        AnalysisStartedAt = DateTime.UtcNow;
        SetUpdated();
    }

    /// <summary>
    /// Marks the analysis as completed successfully
    /// </summary>
    public void CompleteAnalysis(int fileCount, int totalLinesOfCode)
    {
        Status = ProjectStatus.Completed;
        FileCount = fileCount;
        TotalLinesOfCode = totalLinesOfCode;
        AnalysisCompletedAt = DateTime.UtcNow;
        SetUpdated();
    }

    /// <summary>
    /// Marks the analysis as failed
    /// </summary>
    public void FailAnalysis(string errorMessage)
    {
        Status = ProjectStatus.Failed;
        ErrorMessage = errorMessage;
        AnalysisCompletedAt = DateTime.UtcNow;
        SetUpdated();
    }

    /// <summary>
    /// Resets a stuck analysis so it can be re-run
    /// </summary>
    public void ResetAnalysis()
    {
        if (Status != ProjectStatus.Analyzing && Status != ProjectStatus.Queued)
            throw new InvalidOperationException("Can only reset projects that are stuck in Analyzing or Queued status");
        
        Status = ProjectStatus.FilesReady;
        AnalysisStartedAt = null;
        AnalysisCompletedAt = null;
        ErrorMessage = null;
        SetUpdated();
    }

    /// <summary>
    /// Sets the API key that owns this project
    /// </summary>
    public void SetApiKey(Guid apiKeyId)
    {
        ApiKeyId = apiKeyId;
    }
}
