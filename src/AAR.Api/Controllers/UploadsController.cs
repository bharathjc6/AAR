// =============================================================================
// AAR.Api - Controllers/UploadsController.cs
// Resumable chunked uploads endpoints
// =============================================================================

using AAR.Application.DTOs;
using AAR.Application.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace AAR.Api.Controllers;

/// <summary>
/// Resumable upload endpoints for large files
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class UploadsController : ControllerBase
{
    private readonly IUploadSessionService _uploadService;
    private readonly ILogger<UploadsController> _logger;

    public UploadsController(
        IUploadSessionService uploadService,
        ILogger<UploadsController> logger)
    {
        _uploadService = uploadService;
        _logger = logger;
    }

    /// <summary>
    /// Initiates a new resumable upload session
    /// </summary>
    /// <param name="request">Upload initialization request</param>
    /// <returns>Upload session details</returns>
    [HttpPost]
    [ProducesResponseType<InitiateUploadResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<InitiateUploadResponse>> InitiateUpload(
        [FromBody] InitiateUploadRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return BadRequest(new { error = "Project name is required" });
        }

        if (request.TotalSizeBytes <= 0)
        {
            return BadRequest(new { error = "TotalSizeBytes must be positive" });
        }

        if (request.TotalParts <= 0)
        {
            return BadRequest(new { error = "TotalParts must be positive" });
        }

        var apiKeyId = GetApiKeyId();
        
        _logger.LogInformation(
            "Initiating upload for {Name} from API key {ApiKeyId}",
            request.Name, apiKeyId);

        try
        {
            var result = await _uploadService.InitiateAsync(request, apiKeyId, cancellationToken);
            return CreatedAtAction(
                nameof(GetStatus),
                new { sessionId = result.SessionId },
                result);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    /// <summary>
    /// Uploads a part of the file
    /// </summary>
    /// <param name="sessionId">Upload session ID</param>
    /// <param name="partNumber">Part number (1-based)</param>
    /// <returns>Upload progress</returns>
    [HttpPut("{sessionId}/parts/{partNumber}")]
    [ProducesResponseType<UploadPartResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [RequestSizeLimit(50 * 1024 * 1024)] // 50MB per part max
    public async Task<ActionResult<UploadPartResponse>> UploadPart(
        Guid sessionId,
        int partNumber,
        CancellationToken cancellationToken)
    {
        if (Request.Body is null)
        {
            return BadRequest(new { error = "No content provided" });
        }

        _logger.LogInformation(
            "Uploading part {Part} for session {Session}",
            partNumber, sessionId);

        try
        {
            var result = await _uploadService.UploadPartAsync(
                sessionId, partNumber, Request.Body, cancellationToken);
            return Ok(result);
        }
        catch (KeyNotFoundException)
        {
            return NotFound(new { error = "Session not found" });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
        catch (ArgumentOutOfRangeException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    /// <summary>
    /// Gets the status of an upload session
    /// </summary>
    /// <param name="sessionId">Upload session ID</param>
    /// <returns>Session status</returns>
    [HttpGet("{sessionId}")]
    [ProducesResponseType<UploadSessionStatusResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<UploadSessionStatusResponse>> GetStatus(
        Guid sessionId,
        CancellationToken cancellationToken)
    {
        var result = await _uploadService.GetStatusAsync(sessionId, cancellationToken);
        
        if (result is null)
            return NotFound(new { error = "Session not found" });

        return Ok(result);
    }

    /// <summary>
    /// Finalizes the upload and creates the project
    /// </summary>
    /// <param name="sessionId">Upload session ID</param>
    /// <param name="autoAnalyze">Whether to automatically start analysis</param>
    /// <returns>Created project details</returns>
    [HttpPost("{sessionId}/finalize")]
    [ProducesResponseType<FinalizeUploadResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<FinalizeUploadResponse>> Finalize(
        Guid sessionId,
        [FromQuery] bool autoAnalyze = true,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "Finalizing upload session {Session}, autoAnalyze={Auto}",
            sessionId, autoAnalyze);

        try
        {
            var result = await _uploadService.FinalizeAsync(sessionId, autoAnalyze, cancellationToken);
            return Ok(result);
        }
        catch (KeyNotFoundException)
        {
            return NotFound(new { error = "Session not found" });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    /// <summary>
    /// Cancels an upload session
    /// </summary>
    /// <param name="sessionId">Upload session ID</param>
    [HttpDelete("{sessionId}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Cancel(
        Guid sessionId,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("Cancelling upload session {Session}", sessionId);
        
        await _uploadService.CancelAsync(sessionId, cancellationToken);
        return NoContent();
    }

    private Guid GetApiKeyId()
    {
        var claim = User.FindFirst("api_key_id")?.Value;
        return claim != null && Guid.TryParse(claim, out var id) ? id : Guid.Empty;
    }
}
