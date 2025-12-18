// =============================================================================
// AAR.Domain - Entities/FileRecord.cs
// Represents a file within a project
// =============================================================================

using AAR.Domain.ValueObjects;

namespace AAR.Domain.Entities;

/// <summary>
/// Represents a source file within a project
/// </summary>
public class FileRecord : BaseEntity
{
    /// <summary>
    /// Project this file belongs to
    /// </summary>
    public Guid ProjectId { get; private set; }
    
    /// <summary>
    /// Relative path within the project
    /// </summary>
    public string RelativePath { get; private set; } = string.Empty;
    
    /// <summary>
    /// File extension (e.g., ".cs", ".json")
    /// </summary>
    public string Extension { get; private set; } = string.Empty;
    
    /// <summary>
    /// File size in bytes
    /// </summary>
    public long FileSize { get; private set; }
    
    /// <summary>
    /// Whether this file was included in the analysis
    /// </summary>
    public bool IsAnalyzed { get; private set; }
    
    /// <summary>
    /// Computed metrics for this file
    /// </summary>
    public FileMetrics? Metrics { get; private set; }
    
    /// <summary>
    /// Hash of the file content (for caching/deduplication)
    /// </summary>
    public string? ContentHash { get; private set; }

    /// <summary>
    /// Navigation property to the project
    /// </summary>
    public Project? Project { get; private set; }
    
    /// <summary>
    /// Findings related to this file
    /// </summary>
    public ICollection<ReviewFinding> Findings { get; private set; } = new List<ReviewFinding>();

    // Private constructor for EF Core
    private FileRecord() { }

    /// <summary>
    /// Creates a new file record
    /// </summary>
    public static FileRecord Create(Guid projectId, string relativePath, long fileSize)
    {
        var extension = Path.GetExtension(relativePath)?.ToLowerInvariant() ?? string.Empty;
        
        return new FileRecord
        {
            ProjectId = projectId,
            RelativePath = relativePath,
            Extension = extension,
            FileSize = fileSize,
            IsAnalyzed = false
        };
    }

    /// <summary>
    /// Sets the metrics after analysis
    /// </summary>
    public void SetMetrics(FileMetrics metrics)
    {
        Metrics = metrics;
        IsAnalyzed = true;
        SetUpdated();
    }

    /// <summary>
    /// Sets the content hash
    /// </summary>
    public void SetContentHash(string hash)
    {
        ContentHash = hash;
    }

    /// <summary>
    /// Checks if this is a C# source file
    /// </summary>
    public bool IsCSharpFile => Extension == ".cs";

    /// <summary>
    /// Checks if this is a project file
    /// </summary>
    public bool IsProjectFile => Extension == ".csproj" || Extension == ".fsproj" || Extension == ".vbproj";

    /// <summary>
    /// Checks if this is a solution file
    /// </summary>
    public bool IsSolutionFile => Extension == ".sln";

    /// <summary>
    /// Checks if this is a configuration file
    /// </summary>
    public bool IsConfigFile => Extension is ".json" or ".xml" or ".yaml" or ".yml" or ".config";
}
