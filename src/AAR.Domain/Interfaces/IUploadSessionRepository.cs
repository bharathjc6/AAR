// =============================================================================
// AAR.Domain - Interfaces/IUploadSessionRepository.cs
// Repository interface for upload sessions
// =============================================================================

using AAR.Domain.Entities;

namespace AAR.Domain.Interfaces;

/// <summary>
/// Repository for upload session operations
/// </summary>
public interface IUploadSessionRepository
{
    /// <summary>
    /// Gets an upload session by ID
    /// </summary>
    Task<UploadSession?> GetByIdAsync(Guid sessionId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets active sessions for an API key
    /// </summary>
    Task<IReadOnlyList<UploadSession>> GetActiveByApiKeyAsync(Guid apiKeyId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets expired sessions
    /// </summary>
    Task<IReadOnlyList<UploadSession>> GetExpiredSessionsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Adds a new session
    /// </summary>
    Task AddAsync(UploadSession session, CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates an existing session
    /// </summary>
    Task UpdateAsync(UploadSession session, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes a session
    /// </summary>
    Task DeleteAsync(Guid sessionId, CancellationToken cancellationToken = default);
}
