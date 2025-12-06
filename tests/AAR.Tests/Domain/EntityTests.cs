using AAR.Domain.Entities;
using AAR.Domain.Enums;
using FluentAssertions;

namespace AAR.Tests.Domain;

public class EntityTests
{
    [Fact]
    public void Project_CreateFromZipUpload_CreatesValidProject()
    {
        // Act
        var project = Project.CreateFromZipUpload("Test Project", "test.zip", "A test project");

        // Assert
        project.Name.Should().Be("Test Project");
        project.OriginalFileName.Should().Be("test.zip");
        project.Description.Should().Be("A test project");
        project.Status.Should().Be(ProjectStatus.Created);
        project.Id.Should().NotBeEmpty();
    }

    [Fact]
    public void Project_CreateFromGitRepo_CreatesValidProject()
    {
        // Act
        var project = Project.CreateFromGitRepo("Git Project", "https://github.com/test/repo", "A git project");

        // Assert
        project.Name.Should().Be("Git Project");
        project.GitRepoUrl.Should().Be("https://github.com/test/repo");
        project.Description.Should().Be("A git project");
        project.Status.Should().Be(ProjectStatus.Created);
    }

    [Fact]
    public void Project_CreateFromGitRepo_WithEmptyUrl_ThrowsArgumentException()
    {
        // Act & Assert
        var action = () => Project.CreateFromGitRepo("Test", "", "Desc");
        action.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Project_StartAnalysis_UpdatesStatusAndTimestamp()
    {
        // Arrange
        var project = Project.CreateFromZipUpload("Test", "test.zip");
        
        // Act
        project.StartAnalysis();

        // Assert
        project.Status.Should().Be(ProjectStatus.Analyzing);
        project.AnalysisStartedAt.Should().NotBeNull();
    }

    [Fact]
    public void Project_CompleteAnalysis_UpdatesStatusAndCounts()
    {
        // Arrange
        var project = Project.CreateFromZipUpload("Test", "test.zip");
        project.StartAnalysis();
        
        // Act
        project.CompleteAnalysis(fileCount: 50, totalLinesOfCode: 5000);

        // Assert
        project.Status.Should().Be(ProjectStatus.Completed);
        project.FileCount.Should().Be(50);
        project.TotalLinesOfCode.Should().Be(5000);
        project.AnalysisCompletedAt.Should().NotBeNull();
    }

    [Fact]
    public void Project_FailAnalysis_SetsErrorMessage()
    {
        // Arrange
        var project = Project.CreateFromZipUpload("Test", "test.zip");
        project.StartAnalysis();
        
        // Act
        project.FailAnalysis("Something went wrong");

        // Assert
        project.Status.Should().Be(ProjectStatus.Failed);
        project.ErrorMessage.Should().Be("Something went wrong");
        project.AnalysisCompletedAt.Should().NotBeNull();
    }

    [Fact]
    public void ReviewFinding_Create_CreatesValidFinding()
    {
        // Arrange
        var projectId = Guid.NewGuid();
        var reportId = Guid.NewGuid();

        // Act
        var finding = ReviewFinding.Create(
            projectId: projectId,
            reportId: reportId,
            agentType: AgentType.CodeQuality,
            category: FindingCategory.Performance,
            severity: Severity.High,
            description: "Performance issue found",
            explanation: "Detailed explanation");

        // Assert
        finding.ProjectId.Should().Be(projectId);
        finding.ReportId.Should().Be(reportId);
        finding.AgentType.Should().Be(AgentType.CodeQuality);
        finding.Category.Should().Be(FindingCategory.Performance);
        finding.Severity.Should().Be(Severity.High);
        finding.Description.Should().Be("Performance issue found");
        finding.Explanation.Should().Be("Detailed explanation");
    }

    [Fact]
    public void ReviewFinding_Create_WithFilePath_SetsFilePath()
    {
        // Act
        var finding = ReviewFinding.Create(
            projectId: Guid.NewGuid(),
            reportId: Guid.NewGuid(),
            agentType: AgentType.Security,
            category: FindingCategory.Security,
            severity: Severity.Critical,
            description: "Security vulnerability",
            explanation: "Explanation",
            filePath: "src/Controllers/ApiController.cs");

        // Assert
        finding.FilePath.Should().Be("src/Controllers/ApiController.cs");
    }

    [Fact]
    public void Report_Create_CreatesValidReport()
    {
        // Arrange
        var projectId = Guid.NewGuid();

        // Act
        var report = Report.Create(projectId);

        // Assert
        report.ProjectId.Should().Be(projectId);
        report.Id.Should().NotBeEmpty();
    }

    [Fact]
    public void Report_UpdateStatistics_SetsAllStatistics()
    {
        // Arrange
        var report = Report.Create(Guid.NewGuid());

        // Act
        report.UpdateStatistics(
            summary: "Test summary",
            recommendations: new List<string> { "Recommendation 1" },
            healthScore: 85,
            highCount: 2,
            mediumCount: 5,
            lowCount: 10,
            durationSeconds: 120);

        // Assert
        report.Summary.Should().Be("Test summary");
        report.HealthScore.Should().Be(85);
        report.HighSeverityCount.Should().Be(2);
        report.MediumSeverityCount.Should().Be(5);
        report.LowSeverityCount.Should().Be(10);
        report.TotalFindingsCount.Should().Be(17);
        report.AnalysisDurationSeconds.Should().Be(120);
    }
}
