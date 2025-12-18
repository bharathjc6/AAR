// =============================================================================
// AAR.Infrastructure - Services/GitService.cs
// Git operations service using LibGit2Sharp
// =============================================================================

using AAR.Application.Interfaces;
using LibGit2Sharp;
using Microsoft.Extensions.Logging;

namespace AAR.Infrastructure.Services;

/// <summary>
/// Git operations service for cloning repositories
/// </summary>
public class GitService : IGitService
{
    private readonly ILogger<GitService> _logger;
    private static readonly string[] AllowedHosts = new[] { "github.com", "gitlab.com", "bitbucket.org", "dev.azure.com" };

    public GitService(ILogger<GitService> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task<string> CloneAsync(
        string repoUrl, 
        string targetDirectory, 
        CancellationToken cancellationToken = default)
    {
        if (!IsValidRepoUrl(repoUrl))
        {
            throw new ArgumentException("Invalid repository URL", nameof(repoUrl));
        }

        _logger.LogInformation("Cloning repository: {RepoUrl} to {TargetDirectory}", repoUrl, targetDirectory);

        try
        {
            // Clone on a background thread to not block
            await Task.Run(() =>
            {
                var cloneOptions = new CloneOptions
                {
                    // Shallow clone to save time and space
                    RecurseSubmodules = false
                };

                Repository.Clone(repoUrl, targetDirectory, cloneOptions);
            }, cancellationToken);

            // Clean up .git directory to save space
            var gitDir = Path.Combine(targetDirectory, ".git");
            if (Directory.Exists(gitDir))
            {
                // Make files writable before deletion
                foreach (var file in Directory.GetFiles(gitDir, "*", SearchOption.AllDirectories))
                {
                    File.SetAttributes(file, FileAttributes.Normal);
                }
                Directory.Delete(gitDir, recursive: true);
            }

            _logger.LogInformation("Repository cloned successfully: {RepoUrl}", repoUrl);
            return targetDirectory;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to clone repository: {RepoUrl}", repoUrl);
            throw;
        }
    }

    /// <inheritdoc/>
    public bool IsValidRepoUrl(string repoUrl)
    {
        if (string.IsNullOrWhiteSpace(repoUrl))
            return false;

        // Must be HTTPS URL for security
        if (!repoUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            return false;

        // Must be a valid URI
        if (!Uri.TryCreate(repoUrl, UriKind.Absolute, out var uri))
            return false;

        // Must be from an allowed host
        return AllowedHosts.Any(h => uri.Host.EndsWith(h, StringComparison.OrdinalIgnoreCase));
    }
}
