// =============================================================================
// AAR.Infrastructure - Services/UploadSessionService.cs
// Service for managing resumable chunked uploads
// =============================================================================

using AAR.Application.Configuration;
using AAR.Application.DTOs;
using AAR.Application.Interfaces;
using AAR.Domain.Entities;
using AAR.Domain.Interfaces;
using AAR.Shared;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AAR.Infrastructure.Services;

/// <summary>
/// Manages resumable chunked uploads with session tracking
/// </summary>
public sealed class UploadSessionService : IUploadSessionService
{
    private readonly IUploadSessionRepository _sessionRepository;
    private readonly IBlobStorageService _blobStorage;
    private readonly IProjectRepository _projectRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ResumableUploadOptions _options;
    private readonly ILogger<UploadSessionService> _logger;

    public UploadSessionService(
        IUploadSessionRepository sessionRepository,
        IBlobStorageService blobStorage,
        IProjectRepository projectRepository,
        IUnitOfWork unitOfWork,
        IOptions<ResumableUploadOptions> options,
        ILogger<UploadSessionService> logger)
    {
        _sessionRepository = sessionRepository;
        _blobStorage = blobStorage;
        _projectRepository = projectRepository;
        _unitOfWork = unitOfWork;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<InitiateUploadResponse> InitiateAsync(
        InitiateUploadRequest request,
        Guid apiKeyId,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "Initiating upload session for {FileName}, {Size} bytes, {Parts} parts",
            request.FileName,
            request.TotalSizeBytes,
            request.TotalParts);

        // Validate part count
        if (request.TotalParts > _options.MaxParts)
        {
            throw new InvalidOperationException($"Too many parts. Maximum is {_options.MaxParts}");
        }

        // Calculate expected part size
        var expectedPartSize = request.TotalSizeBytes / request.TotalParts;
        if (expectedPartSize < _options.MinPartSizeBytes && request.TotalParts > 1)
        {
            throw new InvalidOperationException(
                $"Part size too small. Minimum is {_options.MinPartSizeBytes / (1024 * 1024)}MB");
        }

        // Create storage path
        var storagePath = $"uploads/{apiKeyId}/{Guid.NewGuid()}";

        // Create session
        var session = UploadSession.Create(
            apiKeyId,
            request.Name,
            request.Description,
            request.FileName,
            request.TotalSizeBytes,
            request.TotalParts,
            storagePath,
            _options.SessionTimeoutMinutes);

        await _sessionRepository.AddAsync(session, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Created upload session {SessionId}", session.Id);

        return new InitiateUploadResponse
        {
            SessionId = session.Id,
            UploadUrl = $"/api/uploads/{session.Id}/parts/{{partNumber}}",
            FinalizeUrl = $"/api/uploads/{session.Id}/finalize",
            ExpiresAt = session.ExpiresAt,
            MaxPartSizeBytes = _options.MaxPartSizeBytes,
            MinPartSizeBytes = _options.MinPartSizeBytes
        };
    }

    public async Task<UploadPartResponse> UploadPartAsync(
        Guid sessionId,
        int partNumber,
        Stream partStream,
        CancellationToken cancellationToken = default)
    {
        var session = await _sessionRepository.GetByIdAsync(sessionId, cancellationToken);
        
        if (session is null)
            throw new KeyNotFoundException($"Upload session {sessionId} not found");

        if (session.Status != UploadSessionStatus.InProgress)
            throw new InvalidOperationException($"Session is not in progress: {session.Status}");

        if (session.IsExpired)
            throw new InvalidOperationException("Upload session has expired");

        if (partNumber < 1 || partNumber > session.TotalParts)
            throw new ArgumentOutOfRangeException(nameof(partNumber), 
                $"Part number must be between 1 and {session.TotalParts}");

        // Check if part already uploaded
        if (session.GetUploadedPartNumbers().Contains(partNumber))
        {
            _logger.LogWarning("Part {Part} already uploaded for session {Session}", partNumber, sessionId);
            return new UploadPartResponse
            {
                PartNumber = partNumber,
                BytesUploaded = 0,
                TotalBytesUploaded = session.BytesUploaded,
                MissingParts = session.GetMissingParts(),
                IsComplete = session.IsComplete
            };
        }

        // Copy stream to memory to get size
        using var memoryStream = new MemoryStream();
        await partStream.CopyToAsync(memoryStream, cancellationToken);
        var partSize = memoryStream.Length;

        // Validate part size (last part can be smaller)
        if (partNumber < session.TotalParts && partSize < _options.MinPartSizeBytes)
        {
            throw new InvalidOperationException(
                $"Part size {partSize} is below minimum {_options.MinPartSizeBytes}");
        }

        if (partSize > _options.MaxPartSizeBytes)
        {
            throw new InvalidOperationException(
                $"Part size {partSize} exceeds maximum {_options.MaxPartSizeBytes}");
        }

        // Store the part
        memoryStream.Position = 0;
        var partPath = $"part-{partNumber:D5}";
        await _blobStorage.UploadAsync(session.StoragePath, partPath, memoryStream, "application/octet-stream", cancellationToken);

        // Update session
        session.MarkPartUploaded(partNumber, partSize);
        await _sessionRepository.UpdateAsync(session, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Uploaded part {Part}/{Total} ({Size} bytes) for session {Session}",
            partNumber, session.TotalParts, partSize, sessionId);

        return new UploadPartResponse
        {
            PartNumber = partNumber,
            BytesUploaded = partSize,
            TotalBytesUploaded = session.BytesUploaded,
            MissingParts = session.GetMissingParts(),
            IsComplete = session.IsComplete
        };
    }

    public async Task<UploadSessionStatusResponse?> GetStatusAsync(
        Guid sessionId,
        CancellationToken cancellationToken = default)
    {
        var session = await _sessionRepository.GetByIdAsync(sessionId, cancellationToken);
        
        if (session is null)
            return null;

        return new UploadSessionStatusResponse
        {
            SessionId = session.Id,
            Status = session.Status.ToString(),
            TotalSizeBytes = session.TotalSizeBytes,
            BytesUploaded = session.BytesUploaded,
            ProgressPercent = session.TotalSizeBytes > 0 
                ? (double)session.BytesUploaded / session.TotalSizeBytes * 100 
                : 0,
            UploadedParts = session.GetUploadedPartNumbers(),
            MissingParts = session.GetMissingParts(),
            ExpiresAt = session.ExpiresAt,
            ProjectId = session.ProjectId
        };
    }

    public async Task<FinalizeUploadResponse> FinalizeAsync(
        Guid sessionId,
        bool autoAnalyze,
        CancellationToken cancellationToken = default)
    {
        var session = await _sessionRepository.GetByIdAsync(sessionId, cancellationToken);
        
        if (session is null)
            throw new KeyNotFoundException($"Upload session {sessionId} not found");

        if (session.Status != UploadSessionStatus.InProgress)
            throw new InvalidOperationException($"Session is not in progress: {session.Status}");

        if (!session.IsComplete)
        {
            var missing = session.GetMissingParts();
            throw new InvalidOperationException(
                $"Upload incomplete. Missing parts: {string.Join(", ", missing.Take(10))}");
        }

        _logger.LogInformation("Finalizing upload session {Session}", sessionId);

        // Combine parts into final file
        var projectFolder = $"projects/{session.ApiKeyId}/{Guid.NewGuid()}";
        
        using var combinedStream = new MemoryStream();
        for (int i = 1; i <= session.TotalParts; i++)
        {
            var partPath = $"part-{i:D5}";
            var partStream = await _blobStorage.DownloadAsync(session.StoragePath, partPath, cancellationToken);
            if (partStream != null)
            {
                await partStream.CopyToAsync(combinedStream, cancellationToken);
                await partStream.DisposeAsync();
            }
        }

        combinedStream.Position = 0;
        var finalPath = await _blobStorage.UploadAsync(projectFolder, session.FileName, combinedStream, "application/zip", cancellationToken);

        // Create project
        var project = Project.CreateFromZipUpload(
            session.ProjectName,
            session.FileName,
            session.ProjectDescription);
        project.SetStoragePath(finalPath);
        project.SetApiKey(session.ApiKeyId);

        await _projectRepository.AddAsync(project, cancellationToken);

        // Update session
        session.MarkFinalized(project.Id, null);
        await _sessionRepository.UpdateAsync(session, cancellationToken);

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        // Clean up parts (async, don't wait)
        _ = CleanupPartsAsync(session.StoragePath, session.TotalParts);

        _logger.LogInformation(
            "Finalized session {Session}, created project {Project}",
            sessionId, project.Id);

        return new FinalizeUploadResponse
        {
            ProjectId = project.Id,
            Name = project.Name,
            Status = (int)project.Status,
            AnalysisQueued = autoAnalyze,
            Message = autoAnalyze 
                ? "Project created and analysis queued"
                : "Project created. Call POST /api/projects/{id}/analyze to start analysis"
        };
    }

    public async Task CancelAsync(Guid sessionId, CancellationToken cancellationToken = default)
    {
        var session = await _sessionRepository.GetByIdAsync(sessionId, cancellationToken);
        
        if (session is null)
            return;

        _logger.LogInformation("Cancelling upload session {Session}", sessionId);

        session.MarkFailed();
        await _sessionRepository.UpdateAsync(session, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        // Clean up parts
        _ = CleanupPartsAsync(session.StoragePath, session.TotalParts);
    }

    public async Task CleanupExpiredSessionsAsync(CancellationToken cancellationToken = default)
    {
        var expiredSessions = await _sessionRepository.GetExpiredSessionsAsync(cancellationToken);

        _logger.LogInformation("Cleaning up {Count} expired upload sessions", expiredSessions.Count);

        foreach (var session in expiredSessions)
        {
            session.MarkExpired();
            await _sessionRepository.UpdateAsync(session, cancellationToken);
            _ = CleanupPartsAsync(session.StoragePath, session.TotalParts);
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);
    }

    private async Task CleanupPartsAsync(string storagePath, int totalParts)
    {
        try
        {
            for (int i = 1; i <= totalParts; i++)
            {
                var partPath = $"part-{i:D5}";
                await _blobStorage.DeleteAsync(storagePath, partPath);
            }
            _logger.LogDebug("Cleaned up parts for {Path}", storagePath);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to clean up parts for {Path}", storagePath);
        }
    }
}
