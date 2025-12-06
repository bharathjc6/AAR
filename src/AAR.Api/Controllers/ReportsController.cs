// =============================================================================
// AAR.Api - Controllers/ReportsController.cs
// API endpoints for report access
// =============================================================================

using AAR.Application.DTOs;
using AAR.Application.Services;
using AAR.Shared;
using Microsoft.AspNetCore.Mvc;

namespace AAR.Api.Controllers;

/// <summary>
/// Controller for report-related operations
/// </summary>
[ApiController]
[Route("api/v1/projects/{projectId:guid}")]
[Produces("application/json")]
public class ReportsController : ControllerBase
{
    private readonly IReportService _reportService;
    private readonly ILogger<ReportsController> _logger;

    public ReportsController(
        IReportService reportService,
        ILogger<ReportsController> logger)
    {
        _reportService = reportService;
        _logger = logger;
    }

    /// <summary>
    /// Gets the analysis report for a project
    /// </summary>
    /// <param name="projectId">Project ID</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Full report with findings</returns>
    [HttpGet("report")]
    [ProducesResponseType(typeof(ReportDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status202Accepted)]
    public async Task<IActionResult> GetReport(Guid projectId, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Fetching report for project: {ProjectId}", projectId);
        
        var result = await _reportService.GetReportAsync(projectId, cancellationToken);

        return result.Match<IActionResult>(
            report => Ok(report),
            error =>
            {
                if (error.Code == "Report.NotReady")
                {
                    return Accepted(new { message = error.Message, projectId });
                }
                
                if (error.Code.Contains("NotFound"))
                {
                    return NotFound(new ErrorResponse { Error = error, TraceId = HttpContext.TraceIdentifier });
                }
                
                return BadRequest(new ErrorResponse { Error = error, TraceId = HttpContext.TraceIdentifier });
            });
    }

    /// <summary>
    /// Downloads the report as a PDF
    /// </summary>
    /// <param name="projectId">Project ID</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>PDF file</returns>
    [HttpGet("report/pdf")]
    [Produces("application/pdf")]
    [ProducesResponseType(typeof(FileContentResult), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetReportPdf(Guid projectId, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Generating PDF report for project: {ProjectId}", projectId);
        
        var result = await _reportService.GetReportPdfAsync(projectId, cancellationToken);

        return result.Match<IActionResult>(
            pdfBytes => File(pdfBytes, "application/pdf", $"report-{projectId}.pdf"),
            error => NotFound(new ErrorResponse { Error = error, TraceId = HttpContext.TraceIdentifier }));
    }

    /// <summary>
    /// Downloads the report as JSON
    /// </summary>
    /// <param name="projectId">Project ID</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>JSON report file</returns>
    [HttpGet("report/json")]
    [Produces("application/json")]
    [ProducesResponseType(typeof(ReportDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetReportJson(Guid projectId, CancellationToken cancellationToken)
    {
        var result = await _reportService.GetReportAsync(projectId, cancellationToken);

        return result.Match<IActionResult>(
            report =>
            {
                var json = System.Text.Json.JsonSerializer.Serialize(report, new System.Text.Json.JsonSerializerOptions
                {
                    WriteIndented = true,
                    PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase
                });
                
                return File(System.Text.Encoding.UTF8.GetBytes(json), "application/json", $"report-{projectId}.json");
            },
            error => NotFound(new ErrorResponse { Error = error, TraceId = HttpContext.TraceIdentifier }));
    }
}
