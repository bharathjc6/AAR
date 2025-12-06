// =============================================================================
// AAR.Application - Interfaces/IGitService.cs
// Abstraction for Git operations
// =============================================================================

namespace AAR.Application.Interfaces;

/// <summary>
/// Interface for Git operations (cloning repositories)
/// </summary>
public interface IGitService
{
    /// <summary>
    /// Clones a Git repository to a local directory
    /// </summary>
    /// <param name="repoUrl">Repository URL (HTTPS)</param>
    /// <param name="targetDirectory">Directory to clone into</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Path to the cloned repository</returns>
    Task<string> CloneAsync(
        string repoUrl, 
        string targetDirectory, 
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Validates a Git repository URL
    /// </summary>
    /// <param name="repoUrl">Repository URL to validate</param>
    /// <returns>True if the URL is valid</returns>
    bool IsValidRepoUrl(string repoUrl);
}
