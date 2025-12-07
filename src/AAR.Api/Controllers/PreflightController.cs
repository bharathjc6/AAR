// =============================================================================
// AAR.Api - Controllers/PreflightController.cs
// Preflight validation endpoint for size/cost estimation
// =============================================================================

using AAR.Application.DTOs;
using AAR.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AAR.Api.Controllers;

/// <summary>
/// Preflight validation endpoints
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class PreflightController : ControllerBase
{
    private readonly IPreflightService _preflightService;
    private readonly ILogger<PreflightController> _logger;

    public PreflightController(
        IPreflightService preflightService,
        ILogger<PreflightController> logger)
    {
        _preflightService = preflightService;
        _logger = logger;
    }

    /// <summary>
    /// Performs preflight validation before upload
    /// </summary>
    /// <param name="request">Preflight request with estimated size info</param>
    /// <returns>Preflight analysis result</returns>
    [HttpPost]
    [ProducesResponseType<PreflightResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<PreflightResponse>> Preflight(
        [FromBody] PreflightRequest request,
        CancellationToken cancellationToken)
    {
        var organizationId = GetOrganizationId();
        
        _logger.LogInformation(
            "Preflight request from {OrgId}: GitUrl={GitUrl}, CompressedSize={Size}",
            organizationId,
            request.GitRepoUrl,
            request.CompressedSizeBytes);

        var result = await _preflightService.AnalyzeAsync(request, organizationId, cancellationToken);

        if (!result.IsAccepted)
        {
            _logger.LogWarning(
                "Preflight rejected: {Code} - {Reason}",
                result.RejectionCode,
                result.RejectionReason);
        }

        return Ok(result);
    }

    /// <summary>
    /// Analyzes an uploaded zip file for preflight validation
    /// </summary>
    /// <param name="file">Zip file to analyze</param>
    /// <returns>Preflight analysis with accurate size/file counts</returns>
    [HttpPost("analyze")]
    [ProducesResponseType<PreflightResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [RequestSizeLimit(100 * 1024 * 1024)] // 100MB for analysis
    public async Task<ActionResult<PreflightResponse>> AnalyzeZip(
        IFormFile file,
        CancellationToken cancellationToken)
    {
        if (file is null || file.Length == 0)
        {
            return BadRequest(new { error = "No file provided" });
        }

        if (!file.FileName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
        {
            return BadRequest(new { error = "Only .zip files are supported" });
        }

        var organizationId = GetOrganizationId();
        
        _logger.LogInformation(
            "Preflight zip analysis from {OrgId}: {FileName}, {Size} bytes",
            organizationId,
            file.FileName,
            file.Length);

        await using var stream = file.OpenReadStream();
        var result = await _preflightService.AnalyzeZipAsync(stream, organizationId, cancellationToken);

        if (!result.IsAccepted)
        {
            _logger.LogWarning(
                "Preflight zip rejected: {Code} - {Reason}",
                result.RejectionCode,
                result.RejectionReason);
        }

        return Ok(result);
    }

    /// <summary>
    /// Gets current limits without analyzing a specific repo
    /// </summary>
    [HttpGet("limits")]
    [AllowAnonymous]
    [ProducesResponseType<PreflightLimits>(StatusCodes.Status200OK)]
    public ActionResult<PreflightLimits> GetLimits()
    {
        // Return current limits - useful for client-side validation
        var limits = new PreflightLimits
        {
            MaxUncompressedSizeBytes = 500 * 1024 * 1024, // 500MB default
            MaxSingleFileSizeBytes = 10 * 1024 * 1024,    // 10MB default
            MaxFilesCount = 10_000,
            SynchronousThresholdBytes = 5 * 1024 * 1024,
            SynchronousMaxFiles = 100,
            MaxCostWithoutApproval = 100m
        };

        return Ok(limits);
    }

    /// <summary>
    /// Estimates cost for given parameters
    /// </summary>
    [HttpGet("estimate")]
    [AllowAnonymous]
    [ProducesResponseType<CostEstimate>(StatusCodes.Status200OK)]
    public ActionResult<CostEstimate> EstimateCost(
        [FromQuery] long sizeBytes,
        [FromQuery] int fileCount)
    {
        if (sizeBytes <= 0 || fileCount <= 0)
        {
            return BadRequest(new { error = "sizeBytes and fileCount must be positive" });
        }

        var cost = _preflightService.EstimateCost(sizeBytes, fileCount);
        var tokens = _preflightService.EstimateTokenCount(sizeBytes);

        return Ok(new CostEstimate
        {
            SizeBytes = sizeBytes,
            FileCount = fileCount,
            EstimatedTokens = tokens,
            EstimatedCost = cost
        });
    }

    private string GetOrganizationId()
    {
        // Try to get from authenticated user context
        var orgClaim = User.FindFirst("org_id")?.Value;
        if (!string.IsNullOrEmpty(orgClaim))
            return orgClaim;

        // Fall back to API key identity
        var apiKeyClaim = User.FindFirst("api_key_id")?.Value;
        if (!string.IsNullOrEmpty(apiKeyClaim))
            return apiKeyClaim;

        // Anonymous/default
        return "anonymous";
    }
}

/// <summary>
/// Cost estimate response
/// </summary>
public record CostEstimate
{
    public long SizeBytes { get; init; }
    public int FileCount { get; init; }
    public long EstimatedTokens { get; init; }
    public decimal EstimatedCost { get; init; }
}
