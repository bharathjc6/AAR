using AAR.Shared;
using FluentAssertions;

namespace AAR.Tests.Shared;

public class ResultTests
{
    [Fact]
    public void Result_Success_CreatesSuccessfulResult()
    {
        // Act
        var result = Result<int>.Success(42);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.IsFailure.Should().BeFalse();
        result.Value.Should().Be(42);
        result.Error.Should().BeNull();
    }

    [Fact]
    public void Result_Failure_CreatesFailedResult()
    {
        // Arrange
        var error = new Error("Test.Error", "Test error message");

        // Act
        var result = Result<int>.Failure(error);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(error);
    }

    [Fact]
    public void Result_FailureWithCodeAndMessage_CreatesFailedResult()
    {
        // Act
        var result = Result<string>.Failure("Code.123", "Error message");

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error!.Code.Should().Be("Code.123");
        result.Error.Message.Should().Be("Error message");
    }

    [Fact]
    public void Result_ImplicitConversion_FromValue_CreatesSuccessResult()
    {
        // Act
        Result<string> result = "test value";

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be("test value");
    }

    [Fact]
    public void Error_WithDetails_AddsDetailsToError()
    {
        // Arrange
        var error = new Error("Test.Error", "Test message");
        var details = new Dictionary<string, object> { { "key", "value" } };

        // Act
        var errorWithDetails = error.WithDetails(details);

        // Assert
        errorWithDetails.Details.Should().NotBeNull();
        errorWithDetails.Details!["key"].Should().Be("value");
        errorWithDetails.Code.Should().Be("Test.Error");
        errorWithDetails.Message.Should().Be("Test message");
    }

    [Fact]
    public void DomainErrors_Project_NotFound_CreatesCorrectError()
    {
        // Arrange
        var projectId = Guid.NewGuid();

        // Act
        var error = DomainErrors.Project.NotFound(projectId);

        // Assert
        error.Code.Should().Be("Project.NotFound");
        error.Message.Should().Contain(projectId.ToString());
    }

    [Fact]
    public void DomainErrors_Project_InvalidZipFile_ReturnsCorrectError()
    {
        // Act
        var error = DomainErrors.Project.InvalidZipFile;

        // Assert
        error.Code.Should().Be("Project.InvalidZipFile");
        error.Message.Should().NotBeEmpty();
    }
}
