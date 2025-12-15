// =============================================================================
// AAR.Application - Interfaces/IStaticAnalyzer.cs
// Phase 1: Extract file metadata without LLM calls
// =============================================================================

using AAR.Domain.Entities;

namespace AAR.Application.Interfaces;

/// <summary>
/// Performs fast static analysis to extract file metrics without LLM.
/// Implements Phase 1 of the cluster-based analysis pipeline.
/// </summary>
public interface IStaticAnalyzer
{
    /// <summary>
    /// Analyze a single file and extract metrics (no LLM calls).
    /// </summary>
    Task<FileSummary> AnalyzeFileAsync(
        string filePath,
        string relativePath,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Analyze all source files in a directory.
    /// Returns a list of file summaries ready for clustering.
    /// </summary>
    Task<List<FileSummary>> AnalyzeProjectAsync(
        string workingDirectory,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Compute a hash of file content for change detection.
    /// Used to avoid re-analyzing unchanged files.
    /// </summary>
    string ComputeFileHash(string filePath);
}
