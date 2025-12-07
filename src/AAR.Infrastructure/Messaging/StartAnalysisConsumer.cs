// =============================================================================
// AAR.Infrastructure - Messaging/StartAnalysisConsumer.cs
// MassTransit consumer for processing analysis commands
// =============================================================================

using AAR.Application.DTOs;
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
    private readonly IRetrievalOrchestrator _retrievalOrchestrator;
    private readonly IStreamingExtractor _streamingExtractor;
    private readonly IJobProgressService _progressService;
    private readonly IAnalysisTelemetry _telemetry;
    private readonly IPublishEndpoint _publishEndpoint;
    private readonly ILogger<StartAnalysisConsumer> _logger;

    public StartAnalysisConsumer(
        IUnitOfWork unitOfWork,
        IAgentOrchestrator orchestrator,
        IBlobStorageService blobService,
        IRetrievalOrchestrator retrievalOrchestrator,
        IStreamingExtractor streamingExtractor,
        IJobProgressService progressService,
        IAnalysisTelemetry telemetry,
        IPublishEndpoint publishEndpoint,
        ILogger<StartAnalysisConsumer> logger)
    {
        _unitOfWork = unitOfWork;
        _orchestrator = orchestrator;
        _blobService = blobService;
        _retrievalOrchestrator = retrievalOrchestrator;
        _streamingExtractor = streamingExtractor;
        _progressService = progressService;
        _telemetry = telemetry;
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
                // Report extraction phase
                await _progressService.ReportProgressAsync(new JobProgressUpdate
                {
                    ProjectId = project.Id,
                    Phase = "Extracting",
                    ProgressPercent = 5,
                    CurrentFile = "Downloading archive...",
                    FilesProcessed = 0,
                    TotalFiles = 0
                }, context.CancellationToken);

                // Download files from blob storage using streaming extractor
                var extractedFiles = await ExtractProjectFilesStreamingAsync(
                    project.StoragePath, workingDirectory, project.Id, context.CancellationToken);

                // Report indexing phase
                await _progressService.ReportProgressAsync(new JobProgressUpdate
                {
                    ProjectId = project.Id,
                    Phase = "Indexing",
                    ProgressPercent = 20,
                    CurrentFile = "Preparing files for analysis...",
                    FilesProcessed = extractedFiles,
                    TotalFiles = extractedFiles
                }, context.CancellationToken);

                // INDEXING STEP: Chunk, embed, and index project files for RAG
                var files = LoadSourceFiles(workingDirectory);
                if (files.Count > 0)
                {
                    _logger.LogInformation("Indexing {FileCount} files for project {ProjectId}", files.Count, project.Id);
                    var indexResult = await _retrievalOrchestrator.IndexProjectAsync(
                        project.Id, files, context.CancellationToken);
                    
                    _logger.LogInformation(
                        "Indexed project: {ChunksCreated} chunks, {Embeddings} embeddings, {Tokens} tokens in {Duration}ms",
                        indexResult.ChunksCreated, indexResult.EmbeddingsGenerated, 
                        indexResult.TotalTokens, indexResult.IndexingTimeMs);
                }

                // Report analyzing phase
                await _progressService.ReportProgressAsync(new JobProgressUpdate
                {
                    ProjectId = project.Id,
                    Phase = "Analyzing",
                    ProgressPercent = 40,
                    CurrentFile = "Running AI analysis agents...",
                    FilesProcessed = files.Count,
                    TotalFiles = files.Count
                }, context.CancellationToken);

                // Run the analysis orchestrator
                var report = await _orchestrator.AnalyzeAsync(project.Id, workingDirectory, context.CancellationToken);

                // Log telemetry
                var telemetrySummary = _telemetry.GetProjectSummary(project.Id);
                _logger.LogInformation(
                    "Analysis telemetry for {ProjectId}: {TotalTokens} tokens, {EmbeddingCalls} embedding calls, ${Cost:F4} estimated cost",
                    project.Id, telemetrySummary.TotalTokensConsumed, 
                    telemetrySummary.EmbeddingCalls, telemetrySummary.EstimatedCostUsd);

                // Report saving phase
                await _progressService.ReportProgressAsync(new JobProgressUpdate
                {
                    ProjectId = project.Id,
                    Phase = "Saving",
                    ProgressPercent = 90,
                    CurrentFile = "Saving report...",
                    FilesProcessed = files.Count,
                    TotalFiles = files.Count
                }, context.CancellationToken);

                // Save the report
                await _unitOfWork.Reports.AddAsync(report, context.CancellationToken);

                // Update project status
                project.CompleteAnalysis(fileCount: files.Count, totalLinesOfCode: 0);
                await _unitOfWork.Projects.UpdateAsync(project, context.CancellationToken);
                await _unitOfWork.SaveChangesAsync(context.CancellationToken);

                var duration = DateTime.UtcNow - startTime;
                
                _logger.LogInformation(
                    "Completed analysis for project {ProjectId}. Report {ReportId} created in {Duration}ms",
                    project.Id, report.Id, duration.TotalMilliseconds);

                // Report completion via progress service
                await _progressService.ReportCompletionAsync(new JobCompletionUpdate
                {
                    ProjectId = project.Id,
                    ReportId = report.Id,
                    IsSuccess = true,
                    ProcessingTimeSeconds = (int)duration.TotalSeconds,
                    Statistics = new JobStatistics
                    {
                        FilesProcessed = files.Count,
                        FindingsCount = report.Findings.Count,
                        HighSeverityCount = report.Findings.Count(f => f.Severity == Domain.Enums.Severity.High || f.Severity == Domain.Enums.Severity.Critical),
                        MediumSeverityCount = report.Findings.Count(f => f.Severity == Domain.Enums.Severity.Medium),
                        LowSeverityCount = report.Findings.Count(f => f.Severity == Domain.Enums.Severity.Low || f.Severity == Domain.Enums.Severity.Info)
                    }
                }, context.CancellationToken);

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

    /// <summary>
    /// Extracts project files using streaming extractor for memory efficiency
    /// </summary>
    private async Task<int> ExtractProjectFilesStreamingAsync(
        string? storagePath,
        string targetDirectory,
        Guid projectId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(storagePath))
        {
            _logger.LogWarning("No storage path specified for project.");
            return 0;
        }

        const string container = "projects";
        var blobName = storagePath;

        _logger.LogDebug("Downloading blob from container '{Container}', path '{BlobName}'", container, blobName);

        var blobStream = await _blobService.DownloadAsync(container, blobName, cancellationToken);
        if (blobStream == null)
        {
            throw new InvalidOperationException($"Failed to download blob {container}/{blobName}");
        }

        var extractedCount = 0;
        await using (blobStream)
        {
            // Use streaming extractor for memory-efficient extraction
            await foreach (var file in _streamingExtractor.ExtractStreamingAsync(
                blobStream,
                targetDirectory,
                maxFileSize: 10 * 1024 * 1024, // 10MB per file
                maxTotalFiles: 10000,
                progressCallback: async (extracted, total, current) =>
                {
                    await _progressService.ReportProgressAsync(new JobProgressUpdate
                    {
                        ProjectId = projectId,
                        Phase = "Extracting",
                        ProgressPercent = 5 + (15.0 * extracted / Math.Max(1, total)),
                        CurrentFile = current,
                        FilesProcessed = extracted,
                        TotalFiles = total
                    }, cancellationToken);
                },
                cancellationToken: cancellationToken))
            {
                extractedCount++;
                _logger.LogDebug("Extracted: {File} ({Size} bytes)", file.RelativePath, file.SizeBytes);
            }
        }

        _logger.LogInformation("Extracted {Count} files to {Directory}", extractedCount, targetDirectory);
        return extractedCount;
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

    private static readonly HashSet<string> SourceExtensions = [
        ".cs", ".ts", ".tsx", ".js", ".jsx", ".py", ".java", ".go", ".rs", 
        ".cpp", ".c", ".h", ".hpp", ".rb", ".php", ".swift", ".kt", ".scala",
        ".json", ".xml", ".yaml", ".yml", ".md", ".txt"
    ];

    private static readonly HashSet<string> ExcludedDirectories = [
        "node_modules", "bin", "obj", ".git", ".vs", ".idea", 
        "packages", "dist", "build", "__pycache__", ".venv", "venv",
        "coverage", ".nyc_output", "TestResults", ".nuget"
    ];

    private Dictionary<string, string> LoadSourceFiles(string workingDirectory)
    {
        var files = new Dictionary<string, string>();

        foreach (var file in Directory.EnumerateFiles(workingDirectory, "*.*", SearchOption.AllDirectories))
        {
            var relativePath = Path.GetRelativePath(workingDirectory, file);
            var extension = Path.GetExtension(file).ToLowerInvariant();

            // Skip non-source files
            if (!SourceExtensions.Contains(extension))
                continue;

            // Skip excluded directories
            if (ExcludedDirectories.Any(dir => 
                relativePath.Contains($"{Path.DirectorySeparatorChar}{dir}{Path.DirectorySeparatorChar}") ||
                relativePath.Contains($"{Path.AltDirectorySeparatorChar}{dir}{Path.AltDirectorySeparatorChar}") ||
                relativePath.StartsWith($"{dir}{Path.DirectorySeparatorChar}") ||
                relativePath.StartsWith($"{dir}{Path.AltDirectorySeparatorChar}")))
                continue;

            try
            {
                var content = File.ReadAllText(file);
                // Skip very large files (>500KB)
                if (content.Length <= 500_000)
                {
                    files[relativePath] = content;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to read file: {File}", file);
            }
        }

        return files;
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
