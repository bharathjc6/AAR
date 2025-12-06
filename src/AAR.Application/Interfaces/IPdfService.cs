// =============================================================================
// AAR.Application - Interfaces/IPdfService.cs
// Abstraction for PDF generation
// =============================================================================

using AAR.Application.DTOs;

namespace AAR.Application.Interfaces;

/// <summary>
/// Interface for PDF report generation
/// </summary>
public interface IPdfService
{
    /// <summary>
    /// Generates a PDF report from a report DTO
    /// </summary>
    /// <param name="report">Report data</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>PDF content as byte array</returns>
    Task<byte[]> GenerateReportPdfAsync(
        ReportDto report, 
        CancellationToken cancellationToken = default);
}
