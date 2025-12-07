// =============================================================================
// AAR.Infrastructure - Messaging/StartAnalysisConsumer.cs
// MassTransit consumer for processing analysis commands
// =============================================================================

using AAR.Application.Interfaces;
using AAR.Application.Messaging;
using AAR.Domain.Interfaces;
using MassTransit;
using Microsoft.Extensions.Logging;
using System.IO.Compression;

namespace AAR.Infrastructure.Messaging;

/// <summary>
/// MassTransit consumer that processes StartAnalysisCommand messages
/// </summary>
public class StartAnalysisConsumer : IConsumer<StartAnalysisCommand>
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IAgentOrchestrator _orchestrator;
    private readonly IBlobStorageService _blobService;
    private readonly IPublishEndpoint _publishEndpoint;
    private readonly ILogger<StartAnalysisConsumer> _logger;

    public StartAnalysisConsumer(
        IUnitOfWork unitOfWork,
        IAgentOrchestrator orchestrator,
        IBlobStorageService blobService,
        IPublishEndpoint publishEndpoint,
        ILogger<StartAnalysisConsumer> logger)
    {
        _unitOfWork = unitOfWork;
        _orchestrator = orchestrator;
        _blobService = blobService;
        _publishEndpoint = publishEndpoint;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<StartAnalysisCommand> context)
    {
        var command = context.Message;
        var startTime = DateTime.UtcNow;
        
        _logger.LogInformation(
            "Processing analysis command for project {ProjectId}, CorrelationId: {CorrelationId}",
            command.ProjectId, command.CorrelationId);

        // Get the project
        var project = await _unitOfWork.Projects.GetByIdAsync(command.ProjectId, context.CancellationToken);
        
        if (project == null)
        {
            _logger.LogWarning("Project {ProjectId} not found. Skipping job.", command.ProjectId);
            
            await _publishEndpoint.Publish(new AnalysisFailedEvent
            {
                ProjectId = command.ProjectId,
                ErrorMessage = "Project not found",
                CorrelationId = command.CorrelationId,
                RetryCount = 0
            }, context.CancellationToken);
            
            return;
        }

        try
        {
            // Publish started event
            await _publishEndpoint.Publish(new AnalysisStartedEvent
            {
                ProjectId = project.Id,
                CorrelationId = command.CorrelationId
            }, context.CancellationToken);

            // Update project status
            project.StartAnalysis();
            await _unitOfWork.Projects.UpdateAsync(project, context.CancellationToken);
            await _unitOfWork.SaveChangesAsync(context.CancellationToken);

            // Download/extract files to temp directory
            var workingDirectory = Path.Combine(Path.GetTempPath(), "aar", project.Id.ToString());
            Directory.CreateDirectory(workingDirectory);

            try
            {
                // Download files from blob storage
                await ExtractProjectFilesAsync(project.StoragePath, workingDirectory, context.CancellationToken);

                // Run the analysis orchestrator
                var report = await _orchestrator.AnalyzeAsync(project.Id, workingDirectory, context.CancellationToken);

                // Save the report
                await _unitOfWork.Reports.AddAsync(report, context.CancellationToken);

                // Update project status
                project.CompleteAnalysis(fileCount: 0, totalLinesOfCode: 0);
                await _unitOfWork.Projects.UpdateAsync(project, context.CancellationToken);
                await _unitOfWork.SaveChangesAsync(context.CancellationToken);

                var duration = DateTime.UtcNow - startTime;
                
                _logger.LogInformation(
                    "Completed analysis for project {ProjectId}. Report {ReportId} created in {Duration}ms",
                    project.Id, report.Id, duration.TotalMilliseconds);

                // Publish completed event
                await _publishEndpoint.Publish(new AnalysisCompletedEvent
                {
                    ProjectId = project.Id,
                    ReportId = report.Id,
                    Success = true,
                    Duration = duration,
                    CorrelationId = command.CorrelationId
                }, context.CancellationToken);
            }
            finally
            {
                // Cleanup temp directory
                CleanupTempDirectory(workingDirectory);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to analyze project {ProjectId}", project.Id);
            
            // Update project status to failed
            project.FailAnalysis(ex.Message);
            await _unitOfWork.Projects.UpdateAsync(project, context.CancellationToken);
            await _unitOfWork.SaveChangesAsync(context.CancellationToken);

            // Publish failed event
            await _publishEndpoint.Publish(new AnalysisFailedEvent
            {
                ProjectId = project.Id,
                ErrorMessage = ex.Message,
                ExceptionType = ex.GetType().Name,
                CorrelationId = command.CorrelationId
            }, context.CancellationToken);

            // Re-throw to let MassTransit handle retry/dead-letter
            throw;
        }
    }

    private async Task ExtractProjectFilesAsync(
        string? storagePath, 
        string targetDirectory, 
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(storagePath))
        {
            _logger.LogWarning("No storage path specified for project.");
            return;
        }

        // The storage path is "{projectId}/{fileName}" 
        // The actual container is always "projects"
        const string container = "projects";
        var blobName = storagePath; // e.g., "guid/TestProject.zip"

        _logger.LogDebug("Downloading blob from container '{Container}', path '{BlobName}'", container, blobName);

        // Download the zip file
        var zipPath = Path.Combine(targetDirectory, "project.zip");
        
        var blobStream = await _blobService.DownloadAsync(container, blobName, cancellationToken);
        if (blobStream == null)
        {
            throw new InvalidOperationException($"Failed to download blob {container}/{blobName}");
        }
        
        await using (blobStream)
        await using (var fileStream = File.Create(zipPath))
        {
            await blobStream.CopyToAsync(fileStream, cancellationToken);
        }

        // Extract the zip file
        ZipFile.ExtractToDirectory(zipPath, targetDirectory, overwriteFiles: true);
        
        // Clean up the zip file
        File.Delete(zipPath);
        
        _logger.LogDebug("Extracted project files to {Directory}", targetDirectory);
    }

    private void CleanupTempDirectory(string directory)
    {
        try
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to cleanup temp directory: {Directory}", directory);
        }
    }
}

/// <summary>
/// Consumer definition with retry and error handling configuration
/// </summary>
public class StartAnalysisConsumerDefinition : ConsumerDefinition<StartAnalysisConsumer>
{
    public StartAnalysisConsumerDefinition()
    {
        // Configure endpoint name
        EndpointName = "analysis-jobs";
        
        // Configure concurrent message limit
        ConcurrentMessageLimit = 5;
    }

    protected override void ConfigureConsumer(
        IReceiveEndpointConfigurator endpointConfigurator,
        IConsumerConfigurator<StartAnalysisConsumer> consumerConfigurator,
        IRegistrationContext context)
    {
        // Configure retry policy
        endpointConfigurator.UseMessageRetry(r => r
            .Incremental(3, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(10)));

        // Configure circuit breaker
        endpointConfigurator.UseCircuitBreaker(cb =>
        {
            cb.TrackingPeriod = TimeSpan.FromMinutes(1);
            cb.TripThreshold = 15;
            cb.ActiveThreshold = 10;
            cb.ResetInterval = TimeSpan.FromMinutes(5);
        });

        // Configure rate limiter (optional, for API rate limits)
        endpointConfigurator.UseRateLimit(10, TimeSpan.FromSeconds(1));
    }
}
