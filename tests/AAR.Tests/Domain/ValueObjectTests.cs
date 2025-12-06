using AAR.Domain.ValueObjects;
using FluentAssertions;

namespace AAR.Tests.Domain;

public class ValueObjectTests
{
    [Theory]
    [InlineData(1, 10)]
    [InlineData(5, 5)]
    [InlineData(100, 200)]
    public void LineRange_Creation_SetsStartAndEndLines(int start, int end)
    {
        // Act
        var lineRange = new LineRange(start, end);

        // Assert
        lineRange.Start.Should().Be(start);
        lineRange.End.Should().Be(end);
    }

    [Theory]
    [InlineData(0, 10)]
    [InlineData(-1, 10)]
    [InlineData(10, 5)]
    public void LineRange_WithInvalidValues_ThrowsArgumentException(int start, int end)
    {
        // Act & Assert
        var action = () => new LineRange(start, end);
        action.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void LineRange_Equality_WorksCorrectly()
    {
        // Arrange
        var range1 = new LineRange(1, 10);
        var range2 = new LineRange(1, 10);
        var range3 = new LineRange(1, 20);

        // Assert
        range1.Should().Be(range2);
        range1.Should().NotBe(range3);
    }

    [Fact]
    public void LineRange_ToString_ReturnsFormattedString()
    {
        // Arrange
        var range = new LineRange(10, 25);

        // Act
        var result = range.ToString();

        // Assert
        result.Should().Contain("10");
        result.Should().Contain("25");
    }

    [Fact]
    public void LineRange_SingleLine_CreatesSingleLineRange()
    {
        // Act
        var range = LineRange.SingleLine(5);

        // Assert
        range.Start.Should().Be(5);
        range.End.Should().Be(5);
        range.LineCount.Should().Be(1);
    }

    [Fact]
    public void FileMetrics_Creation_SetsAllProperties()
    {
        // Arrange & Act
        var metrics = new FileMetrics
        {
            LinesOfCode = 150,
            TotalLines = 200,
            CyclomaticComplexity = 12,
            MethodCount = 8,
            TypeCount = 2,
            NamespaceCount = 3
        };

        // Assert
        metrics.LinesOfCode.Should().Be(150);
        metrics.TotalLines.Should().Be(200);
        metrics.CyclomaticComplexity.Should().Be(12);
        metrics.MethodCount.Should().Be(8);
        metrics.TypeCount.Should().Be(2);
        metrics.NamespaceCount.Should().Be(3);
    }

    [Fact]
    public void FileMetrics_Empty_ReturnsZeroValues()
    {
        // Act
        var metrics = FileMetrics.Empty;

        // Assert
        metrics.LinesOfCode.Should().Be(0);
        metrics.TotalLines.Should().Be(0);
        metrics.CyclomaticComplexity.Should().Be(0);
        metrics.MethodCount.Should().Be(0);
        metrics.TypeCount.Should().Be(0);
        metrics.NamespaceCount.Should().Be(0);
    }

    [Fact]
    public void FileMetrics_Equality_WorksCorrectly()
    {
        // Arrange
        var metrics1 = new FileMetrics { LinesOfCode = 100, CyclomaticComplexity = 5, MethodCount = 10, TypeCount = 2 };
        var metrics2 = new FileMetrics { LinesOfCode = 100, CyclomaticComplexity = 5, MethodCount = 10, TypeCount = 2 };
        var metrics3 = new FileMetrics { LinesOfCode = 100, CyclomaticComplexity = 5, MethodCount = 10, TypeCount = 3 };

        // Assert
        metrics1.Should().Be(metrics2);
        metrics1.Should().NotBe(metrics3);
    }
}
