// =============================================================================
// AAR.Application - Services/ProjectService.cs
// Application service for project-related operations
// =============================================================================

using AAR.Application.DTOs;
using AAR.Application.Interfaces;
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
    private readonly IQueueService _queueService;
    private readonly IGitService _gitService;
    private readonly ILogger<ProjectService> _logger;

    private const string ProjectsContainer = "projects";
    private const string AnalysisQueue = "analysis-jobs";

    public ProjectService(
        IUnitOfWork unitOfWork,
        IBlobStorageService blobStorage,
        IQueueService queueService,
        IGitService gitService,
        ILogger<ProjectService> logger)
    {
        _unitOfWork = unitOfWork;
        _blobStorage = blobStorage;
        _queueService = queueService;
        _gitService = gitService;
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

                await using var zipStream = File.OpenRead(zipPath);
                var storagePath = $"{project.Id}/repo.zip";
                
                await _blobStorage.UploadAsync(
                    ProjectsContainer,
                    storagePath,
                    zipStream,
                    "application/zip",
                    cancellationToken);

                project.SetStoragePath(storagePath);

                // Cleanup
                File.Delete(zipPath);
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

            // Enqueue analysis job
            var jobMessage = new AnalysisJobMessage
            {
                ProjectId = projectId,
                EnqueuedAt = DateTime.UtcNow
            };

            await _queueService.EnqueueAsync(AnalysisQueue, jobMessage, cancellationToken: cancellationToken);

            _logger.LogInformation("Analysis job enqueued for project: {ProjectId}", projectId);

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

    Task<Result<ProjectDetailDto>> GetProjectAsync(
        Guid projectId,
        CancellationToken cancellationToken = default);

    Task<PagedResult<ProjectSummaryDto>> GetProjectsAsync(
        PaginationParams pagination,
        Guid? apiKeyId = null,
        CancellationToken cancellationToken = default);
}
