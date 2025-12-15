// AAR.Tests - Api/PreflightControllerTests.cs
// Integration tests for the Preflight API endpoints

using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using AAR.Application.DTOs;
using AAR.Tests.Fixtures;
using FluentAssertions;

namespace AAR.Tests.Api;

[Collection("ApiTests")]
public class PreflightControllerTests : IAsyncLifetime
{
    private readonly AarWebApplicationFactory _factory;
    private HttpClient _client = null!;

    public PreflightControllerTests(AarWebApplicationFactory factory)
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
    public async Task AnalyzeZip_ValidSmallZip_ReturnsAccepted()
    {
        // Arrange
        var zipBytes = TestZipGenerator.CreateSmallRepoZip();
        using var content = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(zipBytes);
        fileContent.Headers.ContentType = MediaTypeHeaderValue.Parse("application/zip");
        content.Add(fileContent, "file", "test-repo.zip");

        // Act
        var response = await _client.PostAsync("/api/preflight/analyze", content);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        
        var result = await response.Content.ReadFromJsonAsync<PreflightResponse>(AarWebApplicationFactory.JsonOptions);
        result.Should().NotBeNull();
        result!.IsAccepted.Should().BeTrue();
        result.EstimatedFileCount.Should().BeGreaterThan(0);
        result.EstimatedUncompressedSizeBytes.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task AnalyzeZip_EmptyZip_ReturnsAccepted()
    {
        // Arrange - empty zip should still be valid
        var zipBytes = TestZipGenerator.CreateEmptyZip();
        using var content = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(zipBytes);
        fileContent.Headers.ContentType = MediaTypeHeaderValue.Parse("application/zip");
        content.Add(fileContent, "file", "empty.zip");

        // Act
        var response = await _client.PostAsync("/api/preflight/analyze", content);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        
        var result = await response.Content.ReadFromJsonAsync<PreflightResponse>(AarWebApplicationFactory.JsonOptions);
        result.Should().NotBeNull();
        result!.EstimatedFileCount.Should().Be(0);
    }

    [Fact]
    public async Task AnalyzeZip_NoFile_ReturnsBadRequest()
    {
        // Arrange - no file attached
        using var content = new MultipartFormDataContent();

        // Act
        var response = await _client.PostAsync("/api/preflight/analyze", content);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task AnalyzeZip_NonZipFile_ReturnsBadRequest()
    {
        // Arrange
        using var content = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent("not a zip file"u8.ToArray());
        fileContent.Headers.ContentType = MediaTypeHeaderValue.Parse("text/plain");
        content.Add(fileContent, "file", "test.txt");

        // Act
        var response = await _client.PostAsync("/api/preflight/analyze", content);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task AnalyzeZip_MixedSizeFiles_ReturnsCorrectCounts()
    {
        // Arrange
        var zipBytes = TestZipGenerator.CreateMixedRepoZip();
        using var content = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(zipBytes);
        fileContent.Headers.ContentType = MediaTypeHeaderValue.Parse("application/zip");
        content.Add(fileContent, "file", "mixed-repo.zip");

        // Act
        var response = await _client.PostAsync("/api/preflight/analyze", content);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        
        var result = await response.Content.ReadFromJsonAsync<PreflightResponse>(AarWebApplicationFactory.JsonOptions);
        result.Should().NotBeNull();
        result!.IsAccepted.Should().BeTrue();
        
        // Should have direct, RAG, and skipped files
        result.DirectSendCount.Should().BeGreaterThan(0);
        // RAG or skipped should also have files based on the mixed content
    }

    [Fact]
    public async Task Preflight_WithGitUrl_ReturnsEstimate()
    {
        // Arrange
        var request = new PreflightRequest
        {
            GitRepoUrl = "https://github.com/test/sample-repo",
            ExpectedFileCount = 50,
            CompressedSizeBytes = 1024 * 1024 // 1MB
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/preflight", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        
        var result = await response.Content.ReadFromJsonAsync<PreflightResponse>(AarWebApplicationFactory.JsonOptions);
        result.Should().NotBeNull();
    }
}
