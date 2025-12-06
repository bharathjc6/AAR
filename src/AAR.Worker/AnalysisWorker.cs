using AAR.Application.DTOs;
using AAR.Application.Interfaces;
using AAR.Domain.Enums;
using AAR.Domain.Interfaces;
using AAR.Worker.Agents;

namespace AAR.Worker;

/// <summary>
/// Background worker service that polls the queue for analysis jobs and processes them.
/// </summary>
public class AnalysisWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<AnalysisWorker> _logger;
    private readonly TimeSpan _pollingInterval = TimeSpan.FromSeconds(5);

    public AnalysisWorker(IServiceScopeFactory scopeFactory, ILogger<AnalysisWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Analysis Worker started. Polling interval: {Interval}s", 
            _pollingInterval.TotalSeconds);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessNextJobAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                // Normal shutdown
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing job. Will retry after interval.");
            }

            await Task.Delay(_pollingInterval, stoppingToken);
        }

        _logger.LogInformation("Analysis Worker stopping.");
    }

    private async Task ProcessNextJobAsync(CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var queueService = scope.ServiceProvider.GetRequiredService<IQueueService>();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var orchestrator = scope.ServiceProvider.GetRequiredService<IAgentOrchestrator>();
        var blobService = scope.ServiceProvider.GetRequiredService<IBlobStorageService>();

        // Dequeue the next job
        var queueMessage = await queueService.DequeueAsync<AnalysisJobMessage>("analysis-jobs", cancellationToken);
        
        if (queueMessage == null)
        {
            _logger.LogDebug("No jobs in queue.");
            return;
        }

        var job = queueMessage.Content;
        _logger.LogInformation("Processing job for project {ProjectId}", job.ProjectId);

        // Get the project
        var project = await unitOfWork.Projects.GetByIdAsync(job.ProjectId, cancellationToken);
        
        if (project == null)
        {
            _logger.LogWarning("Project {ProjectId} not found. Skipping job.", job.ProjectId);
            return;
        }

        try
        {
            // Update project status using domain method
            project.StartAnalysis();
            await unitOfWork.Projects.UpdateAsync(project, cancellationToken);
            await unitOfWork.SaveChangesAsync(cancellationToken);

            // Download/extract files to temp directory
            var workingDirectory = Path.Combine(Path.GetTempPath(), "aar", project.Id.ToString());
            Directory.CreateDirectory(workingDirectory);

            try
            {
                // Download files from blob storage
                await ExtractProjectFilesAsync(blobService, project.StoragePath, workingDirectory, cancellationToken);

                // Run the analysis orchestrator
                var report = await orchestrator.AnalyzeAsync(project.Id, workingDirectory, cancellationToken);

                // Save the report
                await unitOfWork.Reports.AddAsync(report, cancellationToken);

                // Update project status using domain method
                project.CompleteAnalysis(fileCount: 0, totalLinesOfCode: 0);
                await unitOfWork.Projects.UpdateAsync(project, cancellationToken);
                await unitOfWork.SaveChangesAsync(cancellationToken);

                _logger.LogInformation("Completed analysis for project {ProjectId}. Report {ReportId} created.", 
                    project.Id, report.Id);
            }
            finally
            {
                // Cleanup temp directory
                try
                {
                    if (Directory.Exists(workingDirectory))
                    {
                        Directory.Delete(workingDirectory, recursive: true);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to cleanup temp directory: {Directory}", workingDirectory);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to analyze project {ProjectId}", project.Id);
            
            // Update project status to failed using domain method
            project.FailAnalysis(ex.Message);
            await unitOfWork.Projects.UpdateAsync(project, cancellationToken);
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
    }

    private async Task ExtractProjectFilesAsync(
        IBlobStorageService blobService, 
        string? storagePath, 
        string targetDirectory, 
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(storagePath))
        {
            _logger.LogWarning("No storage path specified for project.");
            return;
        }

        // Parse container and blob name from storage path
        // Storage path is typically in format: "container/blob-name" or just "blob-name"
        var parts = storagePath.Split('/', 2);
        var containerName = parts.Length > 1 ? parts[0] : "projects";
        var blobName = parts.Length > 1 ? parts[1] : parts[0];

        // Download the zip file
        var zipBytes = await blobService.DownloadBytesAsync(containerName, blobName, cancellationToken);
        
        if (zipBytes == null)
        {
            _logger.LogWarning("Failed to download project files from {StoragePath}", storagePath);
            return;
        }

        var zipPath = Path.Combine(targetDirectory, "source.zip");
        
        await File.WriteAllBytesAsync(zipPath, zipBytes, cancellationToken);
        
        // Extract
        System.IO.Compression.ZipFile.ExtractToDirectory(zipPath, targetDirectory, overwriteFiles: true);
        
        // Delete the zip after extraction
        File.Delete(zipPath);
        
        _logger.LogDebug("Extracted project files to {Directory}", targetDirectory);
    }
}
