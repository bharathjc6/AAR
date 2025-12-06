// =============================================================================
// AAR.Application - Interfaces/IVirusScanService.cs
// Virus scanning service abstraction
// =============================================================================

namespace AAR.Application.Interfaces;

/// <summary>
/// Result of a virus scan operation
/// </summary>
public record VirusScanResult(
    bool IsClean,
    bool ScanPerformed,
    string? ThreatName = null,
    string? ErrorMessage = null);

/// <summary>
/// Abstraction for virus/malware scanning service
/// </summary>
public interface IVirusScanService
{
    /// <summary>
    /// Scans a file stream for viruses/malware
    /// </summary>
    /// <param name="stream">File content stream</param>
    /// <param name="fileName">Original file name (for logging/detection hints)</param>
    /// <returns>Scan result indicating if file is clean</returns>
    Task<VirusScanResult> ScanAsync(Stream stream, string fileName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Scans a file on disk for viruses/malware
    /// </summary>
    /// <param name="filePath">Path to file on disk</param>
    /// <returns>Scan result indicating if file is clean</returns>
    Task<VirusScanResult> ScanFileAsync(string filePath, CancellationToken cancellationToken = default);

    /// <summary>
    /// Indicates if the virus scan service is available and configured
    /// </summary>
    bool IsAvailable { get; }
}
