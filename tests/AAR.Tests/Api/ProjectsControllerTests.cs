// AAR.Tests - Api/ProjectsControllerTests.cs
// Integration tests for the Projects API endpoints

using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using AAR.Api.Controllers;
using AAR.Application.DTOs;
using AAR.Shared;
using AAR.Tests.Fixtures;
using FluentAssertions;

namespace AAR.Tests.Api;

[Collection("ApiTests")]
public class ProjectsControllerTests : IAsyncLifetime
{
    private readonly AarWebApplicationFactory _factory;
    private HttpClient _client = null!;

    public ProjectsControllerTests(AarWebApplicationFactory factory)
    {
        _factory = factory;
    }

    public async Task InitializeAsync()
    {
        await _factory.InitializeDatabaseAsync();
        _client = _factory.CreateAuthenticatedClient();
        // No longer using mocks - tests use real Ollama and Qdrant
    }

    public Task DisposeAsync()
    {
        _client.Dispose();
        return Task.CompletedTask;
    }

    [Fact]
    public async Task CreateFromZip_ValidRequest_Returns201()
    {
        // Arrange
        var zipBytes = TestZipGenerator.CreateSmallRepoZip();
        using var content = new MultipartFormDataContent();
        content.Add(new StringContent("Test Project"), "name");
        content.Add(new StringContent("A test project description"), "description");
        
        var fileContent = new ByteArrayContent(zipBytes);
        fileContent.Headers.ContentType = MediaTypeHeaderValue.Parse("application/zip");
        content.Add(fileContent, "file", "test-repo.zip");

        // Act
        var response = await _client.PostAsync("/api/v1/projects", content);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        
        var result = await response.Content.ReadFromJsonAsync<ProjectCreatedResponse>(AarWebApplicationFactory.JsonOptions);
        result.Should().NotBeNull();
        result!.ProjectId.Should().NotBe(Guid.Empty);
        result.Status.Should().BeDefined();
        
        // Note: No longer checking mock blob storage - using real file system storage
    }

    [Fact]
    public async Task CreateFromZip_NoFile_Returns400()
    {
        // Arrange
        using var content = new MultipartFormDataContent();
        content.Add(new StringContent("Test Project"), "name");

        // Act
        var response = await _client.PostAsync("/api/v1/projects", content);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task CreateFromZip_NoName_Returns400()
    {
        // Arrange
        var zipBytes = TestZipGenerator.CreateSmallRepoZip();
        using var content = new MultipartFormDataContent();
        // Missing name
        var fileContent = new ByteArrayContent(zipBytes);
        fileContent.Headers.ContentType = MediaTypeHeaderValue.Parse("application/zip");
        content.Add(fileContent, "file", "test-repo.zip");

        // Act
        var response = await _client.PostAsync("/api/v1/projects", content);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task CreateFromGit_ValidUrl_Returns201()
    {
        // Arrange
        var request = new CreateProjectFromGitRequest
        {
            Name = "Git Project",
            GitRepoUrl = "https://github.com/test/sample-repo",
            Description = "A project from git"
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/v1/projects/git", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        
        var result = await response.Content.ReadFromJsonAsync<ProjectCreatedResponse>(AarWebApplicationFactory.JsonOptions);
        result.Should().NotBeNull();
        result!.ProjectId.Should().NotBe(Guid.Empty);
    }

    [Fact]
    public async Task CreateFromGit_InvalidUrl_Returns400()
    {
        // Arrange
        var request = new CreateProjectFromGitRequest
        {
            Name = "Git Project",
            GitRepoUrl = "not-a-valid-url",
            Description = "Invalid git URL"
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/v1/projects/git", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task GetProject_ExistingProject_Returns200()
    {
        // Arrange - first create a project
        var projectId = await CreateTestProject();

        // Act
        var response = await _client.GetAsync($"/api/v1/projects/{projectId}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        
        var result = await response.Content.ReadFromJsonAsync<ProjectDetailDto>(AarWebApplicationFactory.JsonOptions);
        result.Should().NotBeNull();
        result!.Id.Should().Be(projectId);
    }

    [Fact]
    public async Task GetProject_NonExistent_Returns404()
    {
        // Arrange
        var randomId = Guid.NewGuid();

        // Act
        var response = await _client.GetAsync($"/api/v1/projects/{randomId}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task ListProjects_WithPagination_ReturnsPagedResult()
    {
        // Arrange - create multiple projects
        for (int i = 0; i < 5; i++)
        {
            await CreateTestProject($"Project {i}");
        }

        // Act
        var response = await _client.GetAsync("/api/v1/projects?page=1&pageSize=3");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        
        var result = await response.Content.ReadFromJsonAsync<PagedResult<ProjectSummaryDto>>(AarWebApplicationFactory.JsonOptions);
        result.Should().NotBeNull();
        result!.Items.Should().HaveCountLessThanOrEqualTo(3);
        result.TotalCount.Should().BeGreaterThanOrEqualTo(5);
    }

    [Fact]
    public async Task StartAnalysis_ValidProject_Returns202()
    {
        // Arrange
        var projectId = await CreateTestProject();

        // Act
        var response = await _client.PostAsync($"/api/v1/projects/{projectId}/analyze", null);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
        
        var result = await response.Content.ReadFromJsonAsync<StartAnalysisResponse>(AarWebApplicationFactory.JsonOptions);
        result.Should().NotBeNull();
        result!.ProjectId.Should().Be(projectId);
    }

    [Fact]
    public async Task StartAnalysis_NonExistentProject_Returns404()
    {
        // Arrange
        var randomId = Guid.NewGuid();

        // Act
        var response = await _client.PostAsync($"/api/v1/projects/{randomId}/analyze", null);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task DeleteProject_ExistingProject_Returns204()
    {
        // Arrange
        var projectId = await CreateTestProject();

        // Act
        var response = await _client.DeleteAsync($"/api/v1/projects/{projectId}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        
        // Verify project is gone
        var getResponse = await _client.GetAsync($"/api/v1/projects/{projectId}");
        getResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task DeleteProject_NonExistent_Returns404()
    {
        // Arrange
        var randomId = Guid.NewGuid();

        // Act
        var response = await _client.DeleteAsync($"/api/v1/projects/{randomId}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task ResetAnalysis_StuckProject_Returns200()
    {
        // Arrange
        var projectId = await CreateTestProject();
        // Start analysis to put it in Analyzing state
        await _client.PostAsync($"/api/v1/projects/{projectId}/analyze", null);

        // Act
        var response = await _client.PostAsync($"/api/v1/projects/{projectId}/reset", null);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        
        var result = await response.Content.ReadFromJsonAsync<ProjectDetailDto>(AarWebApplicationFactory.JsonOptions);
        result.Should().NotBeNull();
        // Status should be reset
    }

    /// <summary>
    /// Helper to create a test project and return its ID
    /// </summary>
    private async Task<Guid> CreateTestProject(string name = "Test Project")
    {
        var zipBytes = TestZipGenerator.CreateSmallRepoZip();
        using var content = new MultipartFormDataContent();
        content.Add(new StringContent(name), "name");
        
        var fileContent = new ByteArrayContent(zipBytes);
        fileContent.Headers.ContentType = MediaTypeHeaderValue.Parse("application/zip");
        content.Add(fileContent, "file", "test-repo.zip");

        var response = await _client.PostAsync("/api/v1/projects", content);
        response.EnsureSuccessStatusCode();
        
        var result = await response.Content.ReadFromJsonAsync<ProjectCreatedResponse>(AarWebApplicationFactory.JsonOptions);
        return result!.ProjectId;
    }
}
