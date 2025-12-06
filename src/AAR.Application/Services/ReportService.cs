// =============================================================================
// AAR.Application - Services/ReportService.cs
// Application service for report-related operations
// =============================================================================

using AAR.Application.DTOs;
using AAR.Application.Interfaces;
using AAR.Domain.Enums;
using AAR.Domain.Interfaces;
using AAR.Shared;
using Microsoft.Extensions.Logging;

namespace AAR.Application.Services;

/// <summary>
/// Application service for managing reports
/// </summary>
public class ReportService : IReportService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IBlobStorageService _blobStorage;
    private readonly IPdfService _pdfService;
    private readonly ILogger<ReportService> _logger;

    private const string ReportsContainer = "reports";

    public ReportService(
        IUnitOfWork unitOfWork,
        IBlobStorageService blobStorage,
        IPdfService pdfService,
        ILogger<ReportService> logger)
    {
        _unitOfWork = unitOfWork;
        _blobStorage = blobStorage;
        _pdfService = pdfService;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task<Result<ReportDto>> GetReportAsync(
        Guid projectId,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Fetching report for project: {ProjectId}", projectId);

        var project = await _unitOfWork.Projects.GetByIdAsync(projectId, cancellationToken);
        
        if (project is null)
        {
            return DomainErrors.Project.NotFound(projectId);
        }

        if (project.Status == ProjectStatus.Analyzing || project.Status == ProjectStatus.Queued)
        {
            return DomainErrors.Report.NotReady;
        }

        var report = await _unitOfWork.Reports.GetWithFindingsAsync(projectId, cancellationToken);
        
        if (report is null)
        {
            return DomainErrors.Report.NotFound(projectId);
        }

        // Get download URLs
        string? pdfUrl = null;
        string? jsonUrl = null;

        if (!string.IsNullOrEmpty(report.PdfReportPath))
        {
            pdfUrl = await _blobStorage.GetDownloadUrlAsync(
                ReportsContainer, 
                report.PdfReportPath, 
                TimeSpan.FromHours(1),
                cancellationToken);
        }

        if (!string.IsNullOrEmpty(report.JsonReportPath))
        {
            jsonUrl = await _blobStorage.GetDownloadUrlAsync(
                ReportsContainer, 
                report.JsonReportPath, 
                TimeSpan.FromHours(1),
                cancellationToken);
        }

        // Map findings by category for statistics
        var findingsByCategory = report.Findings
            .GroupBy(f => f.Category.ToString())
            .ToDictionary(g => g.Key, g => g.Count());

        return new ReportDto
        {
            Id = report.Id,
            ProjectId = report.ProjectId,
            ProjectName = project.Name,
            Summary = report.Summary,
            Recommendations = report.Recommendations,
            HealthScore = report.HealthScore,
            Statistics = new StatisticsDto
            {
                TotalFiles = project.FileCount,
                AnalyzedFiles = project.FileCount, // All files analyzed
                TotalLinesOfCode = project.TotalLinesOfCode,
                HighSeverityCount = report.HighSeverityCount,
                MediumSeverityCount = report.MediumSeverityCount,
                LowSeverityCount = report.LowSeverityCount,
                TotalFindingsCount = report.TotalFindingsCount,
                FindingsByCategory = findingsByCategory
            },
            Findings = report.Findings.Select(f => new FindingDto
            {
                Id = f.Id,
                FilePath = f.FilePath,
                LineRange = f.LineRange is not null ? new LineRangeDto
                {
                    Start = f.LineRange.Start,
                    End = f.LineRange.End
                } : null,
                Category = f.Category,
                Severity = f.Severity,
                AgentType = f.AgentType,
                Description = f.Description,
                Explanation = f.Explanation,
                SuggestedFix = f.SuggestedFix,
                FixedCodeSnippet = f.FixedCodeSnippet,
                OriginalCodeSnippet = f.OriginalCodeSnippet
            }).OrderByDescending(f => f.Severity).ToList(),
            ReportVersion = report.ReportVersion,
            AnalysisDurationSeconds = report.AnalysisDurationSeconds,
            GeneratedAt = report.CreatedAt,
            PdfDownloadUrl = pdfUrl,
            JsonDownloadUrl = jsonUrl
        };
    }

    /// <inheritdoc/>
    public async Task<Result<byte[]>> GetReportPdfAsync(
        Guid projectId,
        CancellationToken cancellationToken = default)
    {
        var reportResult = await GetReportAsync(projectId, cancellationToken);
        
        if (reportResult.IsFailure)
        {
            return Result<byte[]>.Failure(reportResult.Error!);
        }

        try
        {
            var pdfBytes = await _pdfService.GenerateReportPdfAsync(reportResult.Value!, cancellationToken);
            return pdfBytes;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating PDF for project: {ProjectId}", projectId);
            return DomainErrors.Report.GenerationFailed;
        }
    }
}

/// <summary>
/// Interface for the report service
/// </summary>
public interface IReportService
{
    Task<Result<ReportDto>> GetReportAsync(
        Guid projectId,
        CancellationToken cancellationToken = default);

    Task<Result<byte[]>> GetReportPdfAsync(
        Guid projectId,
        CancellationToken cancellationToken = default);
}
