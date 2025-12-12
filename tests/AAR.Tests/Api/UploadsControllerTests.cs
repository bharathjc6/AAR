// AAR.Tests - Api/UploadsControllerTests.cs
// Integration tests for the resumable upload API endpoints

using System.Net;
using System.Net.Http.Json;
using AAR.Application.DTOs;
using AAR.Tests.Fixtures;
using FluentAssertions;

namespace AAR.Tests.Api;

[Collection("ApiTests")]
public class UploadsControllerTests : IAsyncLifetime
{
    private readonly AarWebApplicationFactory _factory;
    private HttpClient _client = null!;

    public UploadsControllerTests(AarWebApplicationFactory factory)
    {
        _factory = factory;
    }

    public async Task InitializeAsync()
    {
        await _factory.InitializeDatabaseAsync();
        _client = _factory.CreateAuthenticatedClient();
        _factory.ResetMocks();
    }

    public Task DisposeAsync()
    {
        _client.Dispose();
        return Task.CompletedTask;
    }

    [Fact]
    public async Task InitiateUpload_ValidRequest_Returns201()
    {
        // Arrange
        var request = new InitiateUploadRequest
        {
            Name = "Test Upload Project",
            Description = "A test project via resumable upload",
            TotalSizeBytes = 1024 * 1024, // 1MB
            TotalParts = 5,
            FileName = "large-repo.zip"
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/uploads", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        
        var result = await response.Content.ReadFromJsonAsync<InitiateUploadResponse>(AarWebApplicationFactory.JsonOptions);
        result.Should().NotBeNull();
        result!.SessionId.Should().NotBe(Guid.Empty);
    }

    [Fact]
    public async Task InitiateUpload_MissingName_Returns400()
    {
        // Arrange
        var request = new InitiateUploadRequest
        {
            Name = "", // Empty name
            TotalSizeBytes = 1024 * 1024,
            TotalParts = 5,
            FileName = "test.zip"
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/uploads", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task InitiateUpload_InvalidTotalParts_Returns400()
    {
        // Arrange
        var request = new InitiateUploadRequest
        {
            Name = "Test Project",
            TotalSizeBytes = 1024 * 1024,
            TotalParts = 0, // Invalid
            FileName = "test.zip"
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/uploads", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task UploadPart_ValidPart_Returns200()
    {
        // Arrange - create session first
        var sessionId = await CreateUploadSession(totalParts: 3);
        var partData = new byte[1024]; // 1KB of data
        new Random(42).NextBytes(partData);

        using var content = new ByteArrayContent(partData);
        content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/octet-stream");

        // Act
        var response = await _client.PutAsync($"/api/uploads/{sessionId}/parts/1", content);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        
        var result = await response.Content.ReadFromJsonAsync<UploadPartResponse>(AarWebApplicationFactory.JsonOptions);
        result.Should().NotBeNull();
        result!.PartNumber.Should().Be(1);
        result.BytesUploaded.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task UploadPart_InvalidSession_Returns404()
    {
        // Arrange
        var randomSessionId = Guid.NewGuid();
        var partData = new byte[1024];
        using var content = new ByteArrayContent(partData);

        // Act
        var response = await _client.PutAsync($"/api/uploads/{randomSessionId}/parts/1", content);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GetStatus_ActiveSession_ReturnsProgress()
    {
        // Arrange
        var sessionId = await CreateUploadSession(totalParts: 3);
        
        // Upload one part
        var partData = new byte[1024];
        using var content = new ByteArrayContent(partData);
        await _client.PutAsync($"/api/uploads/{sessionId}/parts/1", content);

        // Act
        var response = await _client.GetAsync($"/api/uploads/{sessionId}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        
        var result = await response.Content.ReadFromJsonAsync<UploadSessionStatusResponse>(AarWebApplicationFactory.JsonOptions);
        result.Should().NotBeNull();
        result!.SessionId.Should().Be(sessionId);
        result.UploadedParts.Should().Contain(1);
    }

    [Fact]
    public async Task GetStatus_NonExistentSession_Returns404()
    {
        // Arrange
        var randomSessionId = Guid.NewGuid();

        // Act
        var response = await _client.GetAsync($"/api/uploads/{randomSessionId}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Finalize_AllPartsUploaded_ReturnsProject()
    {
        // Arrange
        var sessionId = await CreateUploadSession(totalParts: 2);
        
        // Create a real zip and split it
        var zipBytes = TestZipGenerator.CreateSmallRepoZip();
        var partSize = zipBytes.Length / 2 + 1;
        
        // Upload part 1
        var part1 = new byte[partSize];
        Array.Copy(zipBytes, 0, part1, 0, Math.Min(partSize, zipBytes.Length));
        using var content1 = new ByteArrayContent(part1);
        await _client.PutAsync($"/api/uploads/{sessionId}/parts/1", content1);
        
        // Upload part 2
        var part2Size = zipBytes.Length - partSize;
        if (part2Size > 0)
        {
            var part2 = new byte[part2Size];
            Array.Copy(zipBytes, partSize, part2, 0, part2Size);
            using var content2 = new ByteArrayContent(part2);
            await _client.PutAsync($"/api/uploads/{sessionId}/parts/2", content2);
        }

        // Act
        var response = await _client.PostAsync($"/api/uploads/{sessionId}/finalize?autoAnalyze=false", null);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        
        var result = await response.Content.ReadFromJsonAsync<FinalizeUploadResponse>(AarWebApplicationFactory.JsonOptions);
        result.Should().NotBeNull();
        result!.ProjectId.Should().NotBe(Guid.Empty);
    }

    [Fact]
    public async Task Finalize_MissingParts_Returns400()
    {
        // Arrange
        var sessionId = await CreateUploadSession(totalParts: 3);
        
        // Only upload part 1
        var partData = new byte[1024];
        using var content = new ByteArrayContent(partData);
        await _client.PutAsync($"/api/uploads/{sessionId}/parts/1", content);

        // Act - try to finalize without all parts
        var response = await _client.PostAsync($"/api/uploads/{sessionId}/finalize", null);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Cancel_ActiveSession_Returns204()
    {
        // Arrange
        var sessionId = await CreateUploadSession(totalParts: 3);

        // Act
        var response = await _client.DeleteAsync($"/api/uploads/{sessionId}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        
        // Verify session is gone
        var getResponse = await _client.GetAsync($"/api/uploads/{sessionId}");
        getResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task FullUploadFlow_SmallFile_Succeeds()
    {
        // Arrange - create a realistic flow
        var zipBytes = TestZipGenerator.CreateMediumRepoZip(5);
        var partCount = 3;
        var partSize = (int)Math.Ceiling(zipBytes.Length / (double)partCount);

        // Step 1: Initiate upload
        var initRequest = new InitiateUploadRequest
        {
            Name = "Full Flow Test",
            TotalSizeBytes = zipBytes.Length,
            TotalParts = partCount,
            FileName = "medium-repo.zip"
        };
        var initResponse = await _client.PostAsJsonAsync("/api/uploads", initRequest);
        initResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var session = await initResponse.Content.ReadFromJsonAsync<InitiateUploadResponse>(AarWebApplicationFactory.JsonOptions);

        // Step 2: Upload all parts
        for (int i = 0; i < partCount; i++)
        {
            var start = i * partSize;
            var length = Math.Min(partSize, zipBytes.Length - start);
            var partData = new byte[length];
            Array.Copy(zipBytes, start, partData, 0, length);

            using var content = new ByteArrayContent(partData);
            var partResponse = await _client.PutAsync(
                $"/api/uploads/{session!.SessionId}/parts/{i + 1}", content);
            partResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        }

        // Step 3: Finalize
        var finalizeResponse = await _client.PostAsync(
            $"/api/uploads/{session!.SessionId}/finalize?autoAnalyze=false", null);
        finalizeResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = await finalizeResponse.Content.ReadFromJsonAsync<FinalizeUploadResponse>(AarWebApplicationFactory.JsonOptions);
        result.Should().NotBeNull();
        result!.ProjectId.Should().NotBe(Guid.Empty);
        result.AnalysisQueued.Should().BeFalse();
    }

    /// <summary>
    /// Helper to create an upload session
    /// </summary>
    private async Task<Guid> CreateUploadSession(int totalParts = 3)
    {
        var request = new InitiateUploadRequest
        {
            Name = "Test Upload",
            TotalSizeBytes = 1024 * totalParts,
            TotalParts = totalParts,
            FileName = "test.zip"
        };

        var response = await _client.PostAsJsonAsync("/api/uploads", request);
        response.EnsureSuccessStatusCode();
        
        var result = await response.Content.ReadFromJsonAsync<InitiateUploadResponse>(AarWebApplicationFactory.JsonOptions);
        return result!.SessionId;
    }
}
