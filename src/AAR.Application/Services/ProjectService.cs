// =============================================================================
// AAR.Application - Services/ProjectService.cs
// Application service for project-related operations
// =============================================================================

using AAR.Application.DTOs;
using AAR.Application.Interfaces;
using AAR.Application.Messaging;
using AAR.Domain.Entities;
using AAR.Domain.Enums;
using AAR.Domain.Interfaces;
using AAR.Shared;
using Microsoft.Extensions.Logging;

namespace AAR.Application.Services;

/// <summary>
/// Application service for managing projects
/// </summary>
public class ProjectService : IProjectService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IBlobStorageService _blobStorage;
    private readonly IMessageBus _messageBus;
    private readonly IGitService _gitService;
    private readonly IVectorStore _vectorStore;
    private readonly ILogger<ProjectService> _logger;

    private const string ProjectsContainer = "projects";

    public ProjectService(
        IUnitOfWork unitOfWork,
        IBlobStorageService blobStorage,
        IMessageBus messageBus,
        IGitService gitService,
        IVectorStore vectorStore,
        ILogger<ProjectService> logger)
    {
        _unitOfWork = unitOfWork;
        _blobStorage = blobStorage;
        _messageBus = messageBus;
        _gitService = gitService;
        _vectorStore = vectorStore;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task<Result<ProjectCreatedResponse>> CreateFromZipAsync(
        Stream zipStream,
        string fileName,
        CreateProjectFromZipRequest request,
        Guid? apiKeyId = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Creating project from zip: {FileName}", fileName);

            // Validate zip file
            if (!fileName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            {
                return DomainErrors.Project.InvalidZipFile;
            }

            // Create project entity
            var project = Project.CreateFromZipUpload(request.Name, fileName, request.Description);
            
            if (apiKeyId.HasValue)
            {
                project.SetApiKey(apiKeyId.Value);
            }

            // Generate storage path
            var storagePath = $"{project.Id}/{fileName}";

            // Upload to blob storage
            await _blobStorage.UploadAsync(
                ProjectsContainer,
                storagePath,
                zipStream,
                "application/zip",
                cancellationToken);

            project.SetStoragePath(storagePath);

            // Save to database
            await _unitOfWork.Projects.AddAsync(project, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            _logger.LogInformation("Project created successfully: {ProjectId}", project.Id);

            return new ProjectCreatedResponse
            {
                ProjectId = project.Id,
                Name = project.Name,
                Status = project.Status,
                CreatedAt = project.CreatedAt
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating project from zip: {FileName}", fileName);
            throw;
        }
    }

    /// <inheritdoc/>
    public async Task<Result<ProjectCreatedResponse>> CreateFromGitAsync(
        CreateProjectFromGitRequest request,
        Guid? apiKeyId = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Creating project from Git: {GitUrl}", request.GitRepoUrl);

            // Validate Git URL
            if (!_gitService.IsValidRepoUrl(request.GitRepoUrl))
            {
                return DomainErrors.Project.InvalidGitUrl;
            }

            // Create project entity
            var project = Project.CreateFromGitRepo(request.Name, request.GitRepoUrl, request.Description);
            
            if (apiKeyId.HasValue)
            {
                project.SetApiKey(apiKeyId.Value);
            }

            // Clone repository to temp location
            var tempPath = Path.Combine(Path.GetTempPath(), "aar", project.Id.ToString());
            Directory.CreateDirectory(tempPath);

            try
            {
                await _gitService.CloneAsync(request.GitRepoUrl, tempPath, cancellationToken);

                // Create a zip from the cloned repo and upload
                var zipPath = Path.Combine(Path.GetTempPath(), "aar", $"{project.Id}.zip");
                System.IO.Compression.ZipFile.CreateFromDirectory(tempPath, zipPath);

                var storagePath = $"{project.Id}/repo.zip";
                
                // Use a proper using block to ensure the stream is disposed before delete
                await using (var zipStream = File.OpenRead(zipPath))
                {
                    await _blobStorage.UploadAsync(
                        ProjectsContainer,
                        storagePath,
                        zipStream,
                        "application/zip",
                        cancellationToken);
                }

                project.SetStoragePath(storagePath);

                // Cleanup - now safe to delete since stream is closed
                try
                {
                    File.Delete(zipPath);
                }
                catch (IOException)
                {
                    // Best effort cleanup - file will be cleaned up by OS later
                }
            }
            finally
            {
                // Cleanup temp directory
                if (Directory.Exists(tempPath))
                {
                    Directory.Delete(tempPath, recursive: true);
                }
            }

            // Save to database
            await _unitOfWork.Projects.AddAsync(project, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            _logger.LogInformation("Project created successfully from Git: {ProjectId}", project.Id);

            return new ProjectCreatedResponse
            {
                ProjectId = project.Id,
                Name = project.Name,
                Status = project.Status,
                CreatedAt = project.CreatedAt
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating project from Git: {GitUrl}", request.GitRepoUrl);
            throw;
        }
    }

    /// <inheritdoc/>
    public async Task<Result<StartAnalysisResponse>> StartAnalysisAsync(
        Guid projectId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Starting analysis for project: {ProjectId}", projectId);

            var project = await _unitOfWork.Projects.GetByIdAsync(projectId, cancellationToken);
            
            if (project is null)
            {
                return DomainErrors.Project.NotFound(projectId);
            }

            if (project.Status == ProjectStatus.Analyzing)
            {
                return DomainErrors.Project.AlreadyAnalyzing;
            }

            if (string.IsNullOrEmpty(project.StoragePath))
            {
                return DomainErrors.Project.NoFilesToAnalyze;
            }

            // Mark as queued
            project.MarkAsQueued();
            await _unitOfWork.Projects.UpdateAsync(project, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            // Send analysis command via MassTransit
            var correlationId = Guid.NewGuid();
            var command = new StartAnalysisCommand
            {
                ProjectId = projectId,
                Priority = 0,
                CorrelationId = correlationId,
                Metadata = new Dictionary<string, string>
                {
                    ["EnqueuedAt"] = DateTime.UtcNow.ToString("O"),
                    ["InitiatedBy"] = "ProjectService"
                }
            };

            await _messageBus.SendAsync(command, cancellationToken: cancellationToken);

            _logger.LogInformation(
                "Analysis command sent for project: {ProjectId} with CorrelationId: {CorrelationId}",
                projectId, correlationId);

            return new StartAnalysisResponse
            {
                ProjectId = projectId,
                Status = project.Status,
                Message = "Analysis job has been queued for processing."
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error starting analysis for project: {ProjectId}", projectId);
            throw;
        }
    }

    /// <inheritdoc/>
    public async Task<Result<ProjectDetailDto>> ResetAnalysisAsync(
        Guid projectId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Resetting analysis for project: {ProjectId}", projectId);

            var project = await _unitOfWork.Projects.GetByIdAsync(projectId, cancellationToken);
            
            if (project is null)
            {
                return DomainErrors.Project.NotFound(projectId);
            }

            if (project.Status != ProjectStatus.Analyzing && project.Status != ProjectStatus.Queued)
            {
                return new Error("Project.CannotReset", "Can only reset projects that are stuck in Analyzing or Queued status");
            }

            project.ResetAnalysis();
            await _unitOfWork.Projects.UpdateAsync(project, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            _logger.LogInformation("Successfully reset analysis for project: {ProjectId}", projectId);

            return MapToDetailDto(project);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error resetting analysis for project: {ProjectId}", projectId);
            throw;
        }
    }

    /// <inheritdoc/>
    public async Task<Result<ProjectDetailDto>> GetProjectAsync(
        Guid projectId,
        CancellationToken cancellationToken = default)
    {
        var project = await _unitOfWork.Projects.GetWithReportAsync(projectId, cancellationToken);
        
        if (project is null)
        {
            return DomainErrors.Project.NotFound(projectId);
        }

        return MapToDetailDto(project);
    }

    /// <inheritdoc/>
    public async Task<PagedResult<ProjectSummaryDto>> GetProjectsAsync(
        PaginationParams pagination,
        Guid? apiKeyId = null,
        CancellationToken cancellationToken = default)
    {
        var projects = await _unitOfWork.Projects.GetPagedAsync(pagination, apiKeyId, cancellationToken);
        return projects.Map(MapToSummaryDto);
    }

    /// <inheritdoc/>
    public async Task<Result<bool>> DeleteProjectAsync(
        Guid projectId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Deleting project: {ProjectId}", projectId);

            var project = await _unitOfWork.Projects.GetByIdAsync(projectId, cancellationToken);
            if (project is null)
            {
                return DomainErrors.Project.NotFound(projectId);
            }

            // Use execution strategy to handle SQL Server retry logic with transactions
            await _unitOfWork.ExecuteInTransactionAsync(async ct =>
            {
                // 1. Delete ReviewFindings explicitly first (has Restrict FK on Report)
                // Must save immediately because EF may not order deletes correctly
                await _unitOfWork.ReviewFindings.DeleteByProjectIdAsync(projectId, ct);
                await _unitOfWork.SaveChangesAsync(ct);

                // 2. Delete vectors from vector store
                await _vectorStore.DeleteByProjectIdAsync(projectId, ct);

                // 3. Delete chunks from database
                await _unitOfWork.Chunks.DeleteByProjectIdAsync(projectId, ct);
                await _unitOfWork.SaveChangesAsync(ct);

                // 4. Delete job checkpoints
                await _unitOfWork.JobCheckpoints.DeleteByProjectIdAsync(projectId, ct);

                // 5. Delete blob storage files
                if (!string.IsNullOrEmpty(project.StoragePath))
                {
                    await _blobStorage.DeleteByPrefixAsync(ProjectsContainer, project.Id.ToString(), ct);
                }

                // 6. Delete project (cascades to Report, FileRecords)
                await _unitOfWork.Projects.DeleteAsync(projectId, ct);
                
                return true;
            }, cancellationToken);

            _logger.LogInformation("Project deleted successfully: {ProjectId}", projectId);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting project: {ProjectId}", projectId);
            return DomainErrors.Validation.InvalidRequest($"Failed to delete project: {ex.Message}");
        }
    }

    private static ProjectDetailDto MapToDetailDto(Project project)
    {
        return new ProjectDetailDto
        {
            Id = project.Id,
            Name = project.Name,
            Description = project.Description,
            GitRepoUrl = project.GitRepoUrl,
            OriginalFileName = project.OriginalFileName,
            Status = project.Status,
            ErrorMessage = project.ErrorMessage,
            CreatedAt = project.CreatedAt,
            AnalysisStartedAt = project.AnalysisStartedAt,
            AnalysisCompletedAt = project.AnalysisCompletedAt,
            FileCount = project.FileCount,
            TotalLinesOfCode = project.TotalLinesOfCode,
            HasReport = project.Report is not null,
            ReportSummary = project.Report is not null ? new ReportSummaryDto
            {
                ReportId = project.Report.Id,
                HealthScore = project.Report.HealthScore,
                HighSeverityCount = project.Report.HighSeverityCount,
                MediumSeverityCount = project.Report.MediumSeverityCount,
                LowSeverityCount = project.Report.LowSeverityCount,
                TotalFindingsCount = project.Report.TotalFindingsCount,
                HasPdfReport = !string.IsNullOrEmpty(project.Report.PdfReportPath)
            } : null
        };
    }

    private static ProjectSummaryDto MapToSummaryDto(Project project)
    {
        return new ProjectSummaryDto
        {
            Id = project.Id,
            Name = project.Name,
            Description = project.Description,
            Status = project.Status,
            CreatedAt = project.CreatedAt,
            AnalysisCompletedAt = project.AnalysisCompletedAt,
            FileCount = project.FileCount,
            TotalLinesOfCode = project.TotalLinesOfCode,
            HealthScore = project.Report?.HealthScore
        };
    }
}

/// <summary>
/// Interface for the project service
/// </summary>
public interface IProjectService
{
    Task<Result<ProjectCreatedResponse>> CreateFromZipAsync(
        Stream zipStream,
        string fileName,
        CreateProjectFromZipRequest request,
        Guid? apiKeyId = null,
        CancellationToken cancellationToken = default);

    Task<Result<ProjectCreatedResponse>> CreateFromGitAsync(
        CreateProjectFromGitRequest request,
        Guid? apiKeyId = null,
        CancellationToken cancellationToken = default);

    Task<Result<StartAnalysisResponse>> StartAnalysisAsync(
        Guid projectId,
        CancellationToken cancellationToken = default);

    Task<Result<ProjectDetailDto>> ResetAnalysisAsync(
        Guid projectId,
        CancellationToken cancellationToken = default);

    Task<Result<ProjectDetailDto>> GetProjectAsync(
        Guid projectId,
        CancellationToken cancellationToken = default);

    Task<PagedResult<ProjectSummaryDto>> GetProjectsAsync(
        PaginationParams pagination,
        Guid? apiKeyId = null,
        CancellationToken cancellationToken = default);

    Task<Result<bool>> DeleteProjectAsync(
        Guid projectId,
        CancellationToken cancellationToken = default);
}
