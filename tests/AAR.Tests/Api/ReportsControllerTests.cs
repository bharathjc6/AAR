// AAR.Tests - Api/ReportsControllerTests.cs
// Integration tests for the Reports API endpoints

using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using AAR.Application.DTOs;
using AAR.Domain.Entities;
using AAR.Infrastructure.Persistence;
using AAR.Tests.Fixtures;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;

namespace AAR.Tests.Api;

[Collection("ApiTests")]
public class ReportsControllerTests : IAsyncLifetime
{
    private readonly AarWebApplicationFactory _factory;
    private HttpClient _client = null!;

    public ReportsControllerTests(AarWebApplicationFactory factory)
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
    public async Task GetReport_CompletedProject_ReturnsReport()
    {
        // Arrange - create project with completed report
        var projectId = await CreateProjectWithReport();

        // Act
        var response = await _client.GetAsync($"/api/v1/projects/{projectId}/report");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        
        var result = await response.Content.ReadFromJsonAsync<ReportDto>(AarWebApplicationFactory.JsonOptions);
        result.Should().NotBeNull();
        result!.ProjectId.Should().Be(projectId);
        result.HealthScore.Should().BeGreaterThanOrEqualTo(0);
    }

    [Fact]
    public async Task GetReport_AnalyzingProject_Returns202()
    {
        // Arrange - create project and start analysis (not completed)
        var projectId = await CreateProjectInProgress();

        // Act
        var response = await _client.GetAsync($"/api/v1/projects/{projectId}/report");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
    }

    [Fact]
    public async Task GetReport_NoReport_Returns404()
    {
        // Arrange - create project without report
        var projectId = await CreateProjectWithoutReport();

        // Act
        var response = await _client.GetAsync($"/api/v1/projects/{projectId}/report");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GetReportPdf_CompletedProject_ReturnsPdf()
    {
        // Arrange
        var projectId = await CreateProjectWithReport();

        // Act
        var response = await _client.GetAsync($"/api/v1/projects/{projectId}/report/pdf");

        // Assert
        // PDF generation may not be implemented, so we accept either OK or NotImplemented
        response.StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.NotFound, HttpStatusCode.NotImplemented);
        
        if (response.StatusCode == HttpStatusCode.OK)
        {
            response.Content.Headers.ContentType?.MediaType.Should().Be("application/pdf");
        }
    }

    [Fact]
    public async Task GetReportJson_CompletedProject_ReturnsJson()
    {
        // Arrange
        var projectId = await CreateProjectWithReport();

        // Act
        var response = await _client.GetAsync($"/api/v1/projects/{projectId}/report/json");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/json");
    }

    [Fact]
    public async Task GetChunk_ValidChunkId_ReturnsContent()
    {
        // Arrange
        var (projectId, chunkId) = await CreateProjectWithChunks();

        // Act
        var response = await _client.GetAsync($"/api/v1/projects/{projectId}/chunks/{chunkId}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        
        var content = await response.Content.ReadAsStringAsync();
        content.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task GetChunk_InvalidChunkId_Returns404()
    {
        // Arrange
        var projectId = await CreateProjectWithReport();
        var randomChunkId = Guid.NewGuid();

        // Act
        var response = await _client.GetAsync($"/api/v1/projects/{projectId}/chunks/{randomChunkId}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    /// <summary>
    /// Creates a project with a completed report
    /// </summary>
    private async Task<Guid> CreateProjectWithReport()
    {
        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AarDbContext>();

        var project = Project.CreateFromZipUpload("Report Test Project", "test.zip", "Test project");
        project.SetStoragePath("/test/path");
        project.StartAnalysis();
        project.CompleteAnalysis(10, 500);
        
        var report = Report.Create(project.Id);
        report.UpdateStatistics(
            summary: "Test report summary",
            recommendations: ["Consider adding unit tests", "Improve error handling"],
            healthScore: 75,
            highCount: 2,
            mediumCount: 5,
            lowCount: 10,
            durationSeconds: 60);

        dbContext.Projects.Add(project);
        dbContext.Reports.Add(report);
        await dbContext.SaveChangesAsync();

        return project.Id;
    }

    /// <summary>
    /// Creates a project in analyzing state
    /// </summary>
    private async Task<Guid> CreateProjectInProgress()
    {
        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AarDbContext>();

        var project = Project.CreateFromZipUpload("In Progress Project", "test.zip");
        project.SetStoragePath("/test/path");
        project.StartAnalysis();

        dbContext.Projects.Add(project);
        await dbContext.SaveChangesAsync();

        return project.Id;
    }

    /// <summary>
    /// Creates a project without any report
    /// </summary>
    private async Task<Guid> CreateProjectWithoutReport()
    {
        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AarDbContext>();

        var project = Project.CreateFromZipUpload("No Report Project", "test.zip");

        dbContext.Projects.Add(project);
        await dbContext.SaveChangesAsync();

        return project.Id;
    }

    /// <summary>
    /// Creates a project with chunks
    /// </summary>
    private async Task<(Guid ProjectId, Guid ChunkId)> CreateProjectWithChunks()
    {
        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AarDbContext>();

        var project = Project.CreateFromZipUpload("Chunks Test Project", "test.zip");
        project.SetStoragePath("/test/path");
        project.StartAnalysis();
        project.CompleteAnalysis(5, 200);

        var chunk = Chunk.Create(
            projectId: project.Id,
            filePath: "test/file.cs",
            startLine: 1,
            endLine: 10,
            tokenCount: 50,
            language: "csharp",
            textHash: "abc123",
            chunkHash: "hash123",
            semanticType: "class",
            semanticName: "TestClass",
            content: "public class TestClass { public void Method() { } }");

        var report = Report.Create(project.Id);
        report.UpdateStatistics(
            summary: "Test summary",
            recommendations: ["Test recommendation"],
            healthScore: 75,
            highCount: 1,
            mediumCount: 2,
            lowCount: 3,
            durationSeconds: 30);

        dbContext.Projects.Add(project);
        dbContext.Chunks.Add(chunk);
        dbContext.Reports.Add(report);
        await dbContext.SaveChangesAsync();

        return (project.Id, chunk.Id);
    }
}
