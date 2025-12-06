// =============================================================================
// AAR.Infrastructure - Services/PdfService.cs
// PDF report generation using QuestPDF
// =============================================================================

using AAR.Application.DTOs;
using AAR.Application.Interfaces;
using AAR.Domain.Enums;
using Microsoft.Extensions.Logging;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace AAR.Infrastructure.Services;

/// <summary>
/// PDF generation service using QuestPDF
/// </summary>
public class PdfService : IPdfService
{
    private readonly ILogger<PdfService> _logger;

    public PdfService(ILogger<PdfService> logger)
    {
        _logger = logger;
        
        // Configure QuestPDF license (Community license for open source)
        QuestPDF.Settings.License = LicenseType.Community;
    }

    /// <inheritdoc/>
    public Task<byte[]> GenerateReportPdfAsync(ReportDto report, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Generating PDF report for project: {ProjectName}", report.ProjectName);

        try
        {
            var document = Document.Create(container =>
            {
                container.Page(page =>
                {
                    page.Size(PageSizes.A4);
                    page.Margin(40);
                    page.DefaultTextStyle(x => x.FontSize(10));

                    page.Header().Element(c => ComposeHeader(c, report));
                    page.Content().Element(c => ComposeContent(c, report));
                    page.Footer().Element(ComposeFooter);
                });
            });

            var pdfBytes = document.GeneratePdf();
            
            _logger.LogInformation("PDF report generated: {Size} bytes", pdfBytes.Length);
            
            return Task.FromResult(pdfBytes);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating PDF report");
            throw;
        }
    }

    private static void ComposeHeader(IContainer container, ReportDto report)
    {
        container.Row(row =>
        {
            row.RelativeItem().Column(col =>
            {
                col.Item().Text("Architecture Review Report")
                    .FontSize(24).Bold().FontColor(Colors.Blue.Darken2);
                    
                col.Item().Text($"Project: {report.ProjectName}")
                    .FontSize(14).SemiBold();
                    
                col.Item().Text($"Generated: {report.GeneratedAt:yyyy-MM-dd HH:mm:ss} UTC")
                    .FontSize(10).FontColor(Colors.Grey.Darken1);
            });

            row.ConstantItem(80).Column(col =>
            {
                col.Item().AlignCenter().Text("Health Score").FontSize(10);
                col.Item().AlignCenter().Text(report.HealthScore.ToString())
                    .FontSize(28).Bold()
                    .FontColor(GetHealthScoreColor(report.HealthScore));
            });
        });

        container.PaddingTop(10).LineHorizontal(2).LineColor(Colors.Blue.Darken2);
    }

    private static void ComposeContent(IContainer container, ReportDto report)
    {
        container.PaddingTop(20).Column(col =>
        {
            // Summary Section
            col.Item().Element(c => ComposeSummarySection(c, report));
            
            col.Item().PaddingTop(15);
            
            // Statistics Section
            col.Item().Element(c => ComposeStatisticsSection(c, report));
            
            col.Item().PaddingTop(15);
            
            // Recommendations Section
            if (report.Recommendations.Any())
            {
                col.Item().Element(c => ComposeRecommendationsSection(c, report));
                col.Item().PaddingTop(15);
            }
            
            // Findings Section
            col.Item().Element(c => ComposeFindingsSection(c, report));
        });
    }

    private static void ComposeSummarySection(IContainer container, ReportDto report)
    {
        container.Column(col =>
        {
            col.Item().Text("Executive Summary").FontSize(16).Bold().FontColor(Colors.Blue.Darken2);
            col.Item().PaddingTop(5).Text(report.Summary).FontSize(10);
        });
    }

    private static void ComposeStatisticsSection(IContainer container, ReportDto report)
    {
        container.Column(col =>
        {
            col.Item().Text("Analysis Statistics").FontSize(16).Bold().FontColor(Colors.Blue.Darken2);
            
            col.Item().PaddingTop(10).Row(row =>
            {
                row.RelativeItem().Column(statCol =>
                {
                    statCol.Item().Text($"Total Files: {report.Statistics.TotalFiles}");
                    statCol.Item().Text($"Analyzed Files: {report.Statistics.AnalyzedFiles}");
                    statCol.Item().Text($"Lines of Code: {report.Statistics.TotalLinesOfCode:N0}");
                });

                row.RelativeItem().Column(statCol =>
                {
                    statCol.Item().Text($"High Severity: {report.Statistics.HighSeverityCount}")
                        .FontColor(Colors.Red.Darken1);
                    statCol.Item().Text($"Medium Severity: {report.Statistics.MediumSeverityCount}")
                        .FontColor(Colors.Orange.Darken1);
                    statCol.Item().Text($"Low Severity: {report.Statistics.LowSeverityCount}")
                        .FontColor(Colors.Yellow.Darken2);
                });

                row.RelativeItem().Column(statCol =>
                {
                    statCol.Item().Text($"Total Findings: {report.Statistics.TotalFindingsCount}");
                    statCol.Item().Text($"Analysis Duration: {report.AnalysisDurationSeconds}s");
                });
            });
        });
    }

    private static void ComposeRecommendationsSection(IContainer container, ReportDto report)
    {
        container.Column(col =>
        {
            col.Item().Text("Recommendations").FontSize(16).Bold().FontColor(Colors.Blue.Darken2);
            
            col.Item().PaddingTop(5).PaddingLeft(10).Column(recCol =>
            {
                foreach (var rec in report.Recommendations)
                {
                    recCol.Item().Row(row =>
                    {
                        row.ConstantItem(15).Text("•");
                        row.RelativeItem().Text(rec);
                    });
                }
            });
        });
    }

    private static void ComposeFindingsSection(IContainer container, ReportDto report)
    {
        container.Column(col =>
        {
            col.Item().Text("Findings").FontSize(16).Bold().FontColor(Colors.Blue.Darken2);
            
            if (!report.Findings.Any())
            {
                col.Item().PaddingTop(5).Text("No findings reported.").Italic();
                return;
            }

            // Group findings by severity
            var groupedFindings = report.Findings
                .GroupBy(f => f.Severity)
                .OrderByDescending(g => g.Key);

            foreach (var group in groupedFindings)
            {
                col.Item().PaddingTop(10);
                col.Item().Text($"{group.Key} Severity ({group.Count()})")
                    .FontSize(12).SemiBold()
                    .FontColor(GetSeverityColor(group.Key));

                foreach (var finding in group.Take(20)) // Limit per severity
                {
                    col.Item().PaddingTop(5).Element(c => ComposeFinding(c, finding));
                }

                if (group.Count() > 20)
                {
                    col.Item().Text($"  ... and {group.Count() - 20} more {group.Key} severity findings")
                        .Italic().FontColor(Colors.Grey.Darken1);
                }
            }
        });
    }

    private static void ComposeFinding(IContainer container, FindingDto finding)
    {
        container.Border(1).BorderColor(Colors.Grey.Lighten2)
            .Padding(8).Column(col =>
            {
                col.Item().Row(row =>
                {
                    row.RelativeItem().Text(finding.Description).SemiBold();
                    row.ConstantItem(80).AlignRight().Text(finding.CategoryText)
                        .FontSize(8).FontColor(Colors.Grey.Darken1);
                });

                if (!string.IsNullOrEmpty(finding.FilePath))
                {
                    var location = finding.LineRange is not null
                        ? $"{finding.FilePath}:{finding.LineRange.Start}-{finding.LineRange.End}"
                        : finding.FilePath;
                    col.Item().Text(location).FontSize(8).FontColor(Colors.Blue.Darken1);
                }

                col.Item().PaddingTop(3).Text(finding.Explanation).FontSize(9);

                if (!string.IsNullOrEmpty(finding.SuggestedFix))
                {
                    col.Item().PaddingTop(3).Text("Fix: " + finding.SuggestedFix)
                        .FontSize(9).Italic().FontColor(Colors.Green.Darken2);
                }
            });
    }

    private static void ComposeFooter(IContainer container)
    {
        container.Column(col =>
        {
            col.Item().LineHorizontal(1).LineColor(Colors.Grey.Lighten1);
            col.Item().PaddingTop(5).Row(row =>
            {
                row.RelativeItem().Text("Autonomous Architecture Reviewer (AAR)")
                    .FontSize(8).FontColor(Colors.Grey.Darken1);
                row.RelativeItem().AlignRight().Text(text =>
                {
                    text.Span("Page ").FontSize(8).FontColor(Colors.Grey.Darken1);
                    text.CurrentPageNumber().FontSize(8);
                    text.Span(" of ").FontSize(8).FontColor(Colors.Grey.Darken1);
                    text.TotalPages().FontSize(8);
                });
            });
        });
    }

    private static Color GetHealthScoreColor(int score) => score switch
    {
        >= 90 => Colors.Green.Darken1,
        >= 75 => Colors.LightGreen.Darken1,
        >= 50 => Colors.Yellow.Darken2,
        >= 25 => Colors.Orange.Darken1,
        _ => Colors.Red.Darken1
    };

    private static Color GetSeverityColor(Severity severity) => severity switch
    {
        Severity.High => Colors.Red.Darken1,
        Severity.Medium => Colors.Orange.Darken1,
        Severity.Low => Colors.Yellow.Darken2,
        _ => Colors.Grey.Darken1
    };
}
