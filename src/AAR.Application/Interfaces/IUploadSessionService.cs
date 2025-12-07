// =============================================================================
// AAR.Application - Interfaces/IUploadSessionService.cs
// Interface for resumable upload session management
// =============================================================================

using AAR.Application.DTOs;

namespace AAR.Application.Interfaces;

/// <summary>
/// Service for managing resumable upload sessions
/// </summary>
public interface IUploadSessionService
{
    /// <summary>
    /// Initiates a new upload session
    /// </summary>
    Task<InitiateUploadResponse> InitiateAsync(
        InitiateUploadRequest request,
        Guid apiKeyId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Uploads a part of the file
    /// </summary>
    Task<UploadPartResponse> UploadPartAsync(
        Guid sessionId,
        int partNumber,
        Stream partStream,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the status of an upload session
    /// </summary>
    Task<UploadSessionStatusResponse?> GetStatusAsync(
        Guid sessionId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Finalizes the upload and creates the project
    /// </summary>
    Task<FinalizeUploadResponse> FinalizeAsync(
        Guid sessionId,
        bool autoAnalyze,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Cancels an upload session
    /// </summary>
    Task CancelAsync(Guid sessionId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Cleans up expired sessions
    /// </summary>
    Task CleanupExpiredSessionsAsync(CancellationToken cancellationToken = default);
}
