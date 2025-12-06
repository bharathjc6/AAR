// =============================================================================
// AAR.Api - Controllers/ProjectsController.cs
// API endpoints for project management
// =============================================================================

using AAR.Application.DTOs;
using AAR.Application.Services;
using AAR.Shared;
using FluentValidation;
using Microsoft.AspNetCore.Mvc;

namespace AAR.Api.Controllers;

/// <summary>
/// Controller for project-related operations
/// </summary>
[ApiController]
[Route("api/v1/[controller]")]
[Produces("application/json")]
public class ProjectsController : ControllerBase
{
    private readonly IProjectService _projectService;
    private readonly IValidator<CreateProjectFromZipRequest> _zipValidator;
    private readonly IValidator<CreateProjectFromGitRequest> _gitValidator;
    private readonly ILogger<ProjectsController> _logger;

    public ProjectsController(
        IProjectService projectService,
        IValidator<CreateProjectFromZipRequest> zipValidator,
        IValidator<CreateProjectFromGitRequest> gitValidator,
        ILogger<ProjectsController> logger)
    {
        _projectService = projectService;
        _zipValidator = zipValidator;
        _gitValidator = gitValidator;
        _logger = logger;
    }

    /// <summary>
    /// Creates a new project from a zip file upload
    /// </summary>
    /// <param name="name">Project name</param>
    /// <param name="description">Optional project description</param>
    /// <param name="file">Zip file containing the repository</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Created project information</returns>
    [HttpPost]
    [Consumes("multipart/form-data")]
    [ProducesResponseType(typeof(ProjectCreatedResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    [RequestSizeLimit(100 * 1024 * 1024)] // 100 MB
    public async Task<IActionResult> CreateFromZip(
        [FromForm] string name,
        [FromForm] string? description,
        IFormFile file,
        CancellationToken cancellationToken)
    {
        if (file is null || file.Length == 0)
        {
            return BadRequest(new ErrorResponse
            {
                Error = DomainErrors.Validation.InvalidRequest("A zip file is required"),
                TraceId = HttpContext.TraceIdentifier
            });
        }

        var request = new CreateProjectFromZipRequest
        {
            Name = name,
            Description = description
        };

        // Validate request
        var validationResult = await _zipValidator.ValidateAsync(request, cancellationToken);
        if (!validationResult.IsValid)
        {
            return BadRequest(new ErrorResponse
            {
                Error = DomainErrors.Validation.InvalidRequest(
                    string.Join("; ", validationResult.Errors.Select(e => e.ErrorMessage))),
                TraceId = HttpContext.TraceIdentifier
            });
        }

        var apiKeyId = GetApiKeyId();
        
        await using var stream = file.OpenReadStream();
        var result = await _projectService.CreateFromZipAsync(
            stream, 
            file.FileName, 
            request, 
            apiKeyId,
            cancellationToken);

        return result.Match<IActionResult>(
            project => CreatedAtAction(nameof(GetProject), new { id = project.ProjectId }, project),
            error => BadRequest(new ErrorResponse { Error = error, TraceId = HttpContext.TraceIdentifier }));
    }

    /// <summary>
    /// Creates a new project from a Git repository URL
    /// </summary>
    /// <param name="request">Git repository details</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Created project information</returns>
    [HttpPost("git")]
    [Consumes("application/json")]
    [ProducesResponseType(typeof(ProjectCreatedResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> CreateFromGit(
        [FromBody] CreateProjectFromGitRequest request,
        CancellationToken cancellationToken)
    {
        // Validate request
        var validationResult = await _gitValidator.ValidateAsync(request, cancellationToken);
        if (!validationResult.IsValid)
        {
            return BadRequest(new ErrorResponse
            {
                Error = DomainErrors.Validation.InvalidRequest(
                    string.Join("; ", validationResult.Errors.Select(e => e.ErrorMessage))),
                TraceId = HttpContext.TraceIdentifier
            });
        }

        var apiKeyId = GetApiKeyId();
        
        var result = await _projectService.CreateFromGitAsync(request, apiKeyId, cancellationToken);

        return result.Match<IActionResult>(
            project => CreatedAtAction(nameof(GetProject), new { id = project.ProjectId }, project),
            error => BadRequest(new ErrorResponse { Error = error, TraceId = HttpContext.TraceIdentifier }));
    }

    /// <summary>
    /// Gets a project by ID
    /// </summary>
    /// <param name="id">Project ID</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Project details</returns>
    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(ProjectDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetProject(Guid id, CancellationToken cancellationToken)
    {
        var result = await _projectService.GetProjectAsync(id, cancellationToken);

        return result.Match<IActionResult>(
            project => Ok(project),
            error => NotFound(new ErrorResponse { Error = error, TraceId = HttpContext.TraceIdentifier }));
    }

    /// <summary>
    /// Lists all projects with pagination
    /// </summary>
    /// <param name="page">Page number (1-based)</param>
    /// <param name="pageSize">Page size (max 100)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Paginated list of projects</returns>
    [HttpGet]
    [ProducesResponseType(typeof(PagedResult<ProjectSummaryDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> ListProjects(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        var pagination = new PaginationParams { Page = page, PageSize = pageSize };
        var apiKeyId = GetApiKeyId();
        
        var result = await _projectService.GetProjectsAsync(pagination, apiKeyId, cancellationToken);
        
        return Ok(result);
    }

    /// <summary>
    /// Starts analysis for a project
    /// </summary>
    /// <param name="id">Project ID</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Analysis status</returns>
    [HttpPost("{id:guid}/analyze")]
    [ProducesResponseType(typeof(StartAnalysisResponse), StatusCodes.Status202Accepted)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> StartAnalysis(Guid id, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Starting analysis for project: {ProjectId}", id);
        
        var result = await _projectService.StartAnalysisAsync(id, cancellationToken);

        return result.Match<IActionResult>(
            response => Accepted(response),
            error => error.Code.Contains("NotFound") 
                ? NotFound(new ErrorResponse { Error = error, TraceId = HttpContext.TraceIdentifier })
                : BadRequest(new ErrorResponse { Error = error, TraceId = HttpContext.TraceIdentifier }));
    }

    private Guid? GetApiKeyId()
    {
        return HttpContext.Items.TryGetValue("ApiKeyId", out var id) && id is Guid guidId 
            ? guidId 
            : null;
    }
}

/// <summary>
/// Standard error response
/// </summary>
public record ErrorResponse
{
    public required Error Error { get; init; }
    public required string TraceId { get; init; }
}
