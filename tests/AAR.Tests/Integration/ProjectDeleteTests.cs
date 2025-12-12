// AAR.Tests - Integration/ProjectDeleteTests.cs
// Tests for cascade delete of projects and related data

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AAR.Application.Interfaces;
using AAR.Application.Messaging;
using AAR.Application.Services;
using AAR.Domain.Entities;
using AAR.Domain.Interfaces;
using AAR.Shared;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace AAR.Tests.Integration;

/// <summary>
/// Tests for project deletion with cascade cleanup of all related data.
/// Verifies that delete operation properly removes:
/// - ReviewFindings
/// - Vector store entries
/// - Chunks
/// - Job checkpoints
/// - Blob storage files
/// - Project (with cascading to Report, FileRecords)
/// </summary>
public class ProjectDeleteTests
{
    private readonly Mock<IUnitOfWork> _unitOfWorkMock;
    private readonly Mock<IBlobStorageService> _blobStorageMock;
    private readonly Mock<IVectorStore> _vectorStoreMock;
    private readonly Mock<IMessageBus> _messageBusMock;
    private readonly Mock<IGitService> _gitServiceMock;
    private readonly Mock<ILogger<ProjectService>> _loggerMock;

    public ProjectDeleteTests()
    {
        _unitOfWorkMock = new Mock<IUnitOfWork>();
        _blobStorageMock = new Mock<IBlobStorageService>();
        _vectorStoreMock = new Mock<IVectorStore>();
        _messageBusMock = new Mock<IMessageBus>();
        _gitServiceMock = new Mock<IGitService>();
        _loggerMock = new Mock<ILogger<ProjectService>>();
    }

    private ProjectService CreateService()
    {
        return new ProjectService(
            _unitOfWorkMock.Object,
            _blobStorageMock.Object,
            _messageBusMock.Object,
            _gitServiceMock.Object,
            _vectorStoreMock.Object,
            _loggerMock.Object);
    }

    [Fact]
    public async Task DeleteProjectAsync_ProjectExists_DeletesAllRelatedData()
    {
        // Arrange
        var projectId = Guid.NewGuid();
        var project = Project.CreateFromZipUpload("Test Project", "test.zip", "Test description");
        
        // Set up the project with a storage path using reflection
        typeof(Project).GetProperty("Id")!.SetValue(project, projectId);
        typeof(Project).GetProperty("StoragePath")!.SetValue(project, $"projects/{projectId}");

        var reviewFindingsRepoMock = new Mock<IReviewFindingRepository>();
        _unitOfWorkMock.Setup(x => x.ReviewFindings).Returns(reviewFindingsRepoMock.Object);

        var chunksRepoMock = new Mock<IChunkRepository>();
        _unitOfWorkMock.Setup(x => x.Chunks).Returns(chunksRepoMock.Object);

        var checkpointsRepoMock = new Mock<IJobCheckpointRepository>();
        _unitOfWorkMock.Setup(x => x.JobCheckpoints).Returns(checkpointsRepoMock.Object);

        var projectsRepoMock = new Mock<IProjectRepository>();
        _unitOfWorkMock.Setup(x => x.Projects).Returns(projectsRepoMock.Object);

        projectsRepoMock.Setup(x => x.GetByIdAsync(projectId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(project);

        // Mock ExecuteInTransactionAsync to actually invoke the callback
        _unitOfWorkMock.Setup(x => x.ExecuteInTransactionAsync(
                It.IsAny<Func<CancellationToken, Task<bool>>>(),
                It.IsAny<CancellationToken>()))
            .Returns<Func<CancellationToken, Task<bool>>, CancellationToken>(
                async (operation, ct) => await operation(ct));

        var service = CreateService();

        // Act
        var result = await service.DeleteProjectAsync(projectId);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.True(result.Value);

        // Verify all cleanup operations were called
        reviewFindingsRepoMock.Verify(
            x => x.DeleteByProjectIdAsync(projectId, It.IsAny<CancellationToken>()),
            Times.Once);

        _vectorStoreMock.Verify(
            x => x.DeleteByProjectIdAsync(projectId, It.IsAny<CancellationToken>()),
            Times.Once);

        chunksRepoMock.Verify(
            x => x.DeleteByProjectIdAsync(projectId, It.IsAny<CancellationToken>()),
            Times.Once);

        checkpointsRepoMock.Verify(
            x => x.DeleteByProjectIdAsync(projectId, It.IsAny<CancellationToken>()),
            Times.Once);

        _blobStorageMock.Verify(
            x => x.DeleteByPrefixAsync("projects", projectId.ToString(), It.IsAny<CancellationToken>()),
            Times.Once);

        projectsRepoMock.Verify(
            x => x.DeleteAsync(projectId, It.IsAny<CancellationToken>()),
            Times.Once);

        _unitOfWorkMock.Verify(x => x.ExecuteInTransactionAsync(
            It.IsAny<Func<CancellationToken, Task<bool>>>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task DeleteProjectAsync_ProjectNotFound_ReturnsError()
    {
        // Arrange
        var projectId = Guid.NewGuid();

        var projectsRepoMock = new Mock<IProjectRepository>();
        projectsRepoMock.Setup(x => x.GetByIdAsync(projectId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Project?)null);
        _unitOfWorkMock.Setup(x => x.Projects).Returns(projectsRepoMock.Object);

        var service = CreateService();

        // Act
        var result = await service.DeleteProjectAsync(projectId);

        // Assert
        Assert.False(result.IsSuccess);
        Assert.Contains("not found", result.Error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task DeleteProjectAsync_TransactionFailure_RollsBack()
    {
        // Arrange
        var projectId = Guid.NewGuid();
        var project = Project.CreateFromZipUpload("Test Project", "test.zip");
        typeof(Project).GetProperty("Id")!.SetValue(project, projectId);

        var projectsRepoMock = new Mock<IProjectRepository>();
        projectsRepoMock.Setup(x => x.GetByIdAsync(projectId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(project);
        _unitOfWorkMock.Setup(x => x.Projects).Returns(projectsRepoMock.Object);

        var reviewFindingsRepoMock = new Mock<IReviewFindingRepository>();
        reviewFindingsRepoMock.Setup(x => x.DeleteByProjectIdAsync(projectId, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Database error"));
        _unitOfWorkMock.Setup(x => x.ReviewFindings).Returns(reviewFindingsRepoMock.Object);

        // Mock ExecuteInTransactionAsync to actually invoke the callback (which will throw)
        _unitOfWorkMock.Setup(x => x.ExecuteInTransactionAsync(
                It.IsAny<Func<CancellationToken, Task<bool>>>(),
                It.IsAny<CancellationToken>()))
            .Returns<Func<CancellationToken, Task<bool>>, CancellationToken>(
                async (operation, ct) => await operation(ct));

        var service = CreateService();

        // Act
        var result = await service.DeleteProjectAsync(projectId);

        // Assert
        Assert.False(result.IsSuccess);
        Assert.Contains("Failed to delete", result.Error.Message);
        Assert.Contains("Database error", result.Error.Message);
    }

    [Fact]
    public async Task DeleteProjectAsync_NoStoragePath_SkipsBlobDeletion()
    {
        // Arrange
        var projectId = Guid.NewGuid();
        var project = Project.CreateFromZipUpload("Test Project", "test.zip");
        typeof(Project).GetProperty("Id")!.SetValue(project, projectId);
        // StoragePath is null/empty by default

        var projectsRepoMock = new Mock<IProjectRepository>();
        projectsRepoMock.Setup(x => x.GetByIdAsync(projectId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(project);
        _unitOfWorkMock.Setup(x => x.Projects).Returns(projectsRepoMock.Object);

        var reviewFindingsRepoMock = new Mock<IReviewFindingRepository>();
        _unitOfWorkMock.Setup(x => x.ReviewFindings).Returns(reviewFindingsRepoMock.Object);

        var chunksRepoMock = new Mock<IChunkRepository>();
        _unitOfWorkMock.Setup(x => x.Chunks).Returns(chunksRepoMock.Object);

        var checkpointsRepoMock = new Mock<IJobCheckpointRepository>();
        _unitOfWorkMock.Setup(x => x.JobCheckpoints).Returns(checkpointsRepoMock.Object);

        // Mock ExecuteInTransactionAsync to actually invoke the callback
        _unitOfWorkMock.Setup(x => x.ExecuteInTransactionAsync(
                It.IsAny<Func<CancellationToken, Task<bool>>>(),
                It.IsAny<CancellationToken>()))
            .Returns<Func<CancellationToken, Task<bool>>, CancellationToken>(
                async (operation, ct) => await operation(ct));

        var service = CreateService();

        // Act
        var result = await service.DeleteProjectAsync(projectId);

        // Assert
        Assert.True(result.IsSuccess);

        // Blob deletion should NOT be called since StoragePath is empty
        _blobStorageMock.Verify(
            x => x.DeleteByPrefixAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }
}
