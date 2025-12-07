// =============================================================================
// AAR.Tests - Infrastructure/SemanticChunkerTests.cs
// Unit tests for semantic chunking
// =============================================================================

using AAR.Application.Interfaces;
using AAR.Infrastructure.Services.Chunking;
using AAR.Shared.Tokenization;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace AAR.Tests.Infrastructure;

public class SemanticChunkerTests
{
    private readonly SemanticChunker _chunker;
    private readonly Mock<ITokenizer> _tokenizerMock;
    private readonly Mock<ITokenizerFactory> _tokenizerFactoryMock;

    public SemanticChunkerTests()
    {
        _tokenizerMock = new Mock<ITokenizer>();
        // Return at least some tokens for any non-empty string
        _tokenizerMock.Setup(t => t.CountTokens(It.IsAny<string>()))
            .Returns<string>(s => string.IsNullOrEmpty(s) ? 0 : Math.Max(1, s.Length / 4));

        _tokenizerFactoryMock = new Mock<ITokenizerFactory>();
        _tokenizerFactoryMock.Setup(f => f.Create()).Returns(_tokenizerMock.Object);

        var options = Options.Create(new ChunkerOptions
        {
            MaxChunkTokens = 500,
            OverlapTokens = 50,
            MinChunkTokens = 1  // Lower minimum for testing
        });

        _chunker = new SemanticChunker(
            _tokenizerFactoryMock.Object,
            options,
            Mock.Of<ILogger<SemanticChunker>>());
    }

    [Fact]
    public async Task ChunkFilesAsync_WithSingleSmallFile_ReturnsSingleChunk()
    {
        // Arrange
        var projectId = Guid.NewGuid();
        var files = new Dictionary<string, string>
        {
            ["Program.cs"] = "Console.WriteLine(\"Hello\");"
        };

        // Act
        var chunks = await _chunker.ChunkFilesAsync(files, projectId);

        // Assert
        chunks.Should().HaveCount(1);
        chunks[0].FilePath.Should().Be("Program.cs");
        chunks[0].StartLine.Should().Be(1);
    }

    [Fact]
    public async Task ChunkFilesAsync_WithCSharpClass_ExtractsClassAsChunk()
    {
        // Arrange
        var projectId = Guid.NewGuid();
        var code = @"
namespace TestApp
{
    public class Calculator
    {
        public int Add(int a, int b)
        {
            return a + b;
        }
    }
}";
        var files = new Dictionary<string, string> { ["Calculator.cs"] = code };

        // Act
        var chunks = await _chunker.ChunkFilesAsync(files, projectId);

        // Assert
        chunks.Should().NotBeEmpty();
        chunks.Should().Contain(c => c.SemanticType == "class" && c.SemanticName == "Calculator");
    }

    [Fact]
    public async Task ChunkFilesAsync_WithNonCSharpFile_UsesSlidingWindow()
    {
        // Arrange
        var projectId = Guid.NewGuid();
        var pythonCode = @"
def hello():
    print('Hello, World!')

def goodbye():
    print('Goodbye!')
";
        var files = new Dictionary<string, string> { ["script.py"] = pythonCode };

        // Act
        var chunks = await _chunker.ChunkFilesAsync(files, projectId);

        // Assert
        chunks.Should().NotBeEmpty();
        chunks[0].Language.Should().Be("python");
    }

    [Fact]
    public async Task ChunkFilesAsync_GeneratesDeterministicChunkIds()
    {
        // Arrange
        var projectId = Guid.NewGuid();
        var files = new Dictionary<string, string>
        {
            ["Test.cs"] = "public class Test { }"
        };

        // Act
        var chunks1 = await _chunker.ChunkFilesAsync(files, projectId);
        var chunks2 = await _chunker.ChunkFilesAsync(files, projectId);

        // Assert
        chunks1[0].ChunkHash.Should().Be(chunks2[0].ChunkHash);
    }

    [Fact]
    public async Task ChunkFilesAsync_WithMultipleFiles_ChunksAll()
    {
        // Arrange
        var projectId = Guid.NewGuid();
        var files = new Dictionary<string, string>
        {
            ["File1.cs"] = "public class A { }",
            ["File2.cs"] = "public class B { }",
            ["File3.cs"] = "public class C { }"
        };

        // Act
        var chunks = await _chunker.ChunkFilesAsync(files, projectId);

        // Assert
        chunks.Should().HaveCountGreaterThanOrEqualTo(3);
        chunks.Select(c => c.FilePath).Distinct().Should().HaveCount(3);
    }

    [Fact]
    public async Task ChunkFilesAsync_WithEmptyFile_SkipsFile()
    {
        // Arrange
        var projectId = Guid.NewGuid();
        var files = new Dictionary<string, string>
        {
            ["Empty.cs"] = "",
            ["Valid.cs"] = "public class Valid { }"
        };

        // Act
        var chunks = await _chunker.ChunkFilesAsync(files, projectId);

        // Assert
        chunks.Should().NotContain(c => c.FilePath == "Empty.cs");
        chunks.Should().Contain(c => c.FilePath == "Valid.cs");
    }

    [Fact]
    public async Task ChunkFilesAsync_PreservesLineNumbers()
    {
        // Arrange
        var projectId = Guid.NewGuid();
        var code = @"// Line 1
// Line 2
public class Test // Line 3
{
    // Line 5
}";
        var files = new Dictionary<string, string> { ["Test.cs"] = code };

        // Act
        var chunks = await _chunker.ChunkFilesAsync(files, projectId);

        // Assert
        var classChunk = chunks.FirstOrDefault(c => c.SemanticType == "class");
        classChunk.Should().NotBeNull();
        classChunk!.StartLine.Should().BeGreaterThanOrEqualTo(1);
        classChunk.EndLine.Should().BeGreaterThan(classChunk.StartLine);
    }

    [Fact]
    public async Task ChunkFilesAsync_WithInterface_IdentifiesInterface()
    {
        // Arrange
        var projectId = Guid.NewGuid();
        var code = @"
public interface IRepository
{
    void Save();
}";
        var files = new Dictionary<string, string> { ["IRepository.cs"] = code };

        // Act
        var chunks = await _chunker.ChunkFilesAsync(files, projectId);

        // Assert
        chunks.Should().Contain(c => c.SemanticType == "interface" && c.SemanticName == "IRepository");
    }

    [Fact]
    public async Task ChunkFilesAsync_WithRecord_IdentifiesRecord()
    {
        // Arrange
        var projectId = Guid.NewGuid();
        var code = "public record Person(string Name, int Age);";
        var files = new Dictionary<string, string> { ["Person.cs"] = code };

        // Act
        var chunks = await _chunker.ChunkFilesAsync(files, projectId);

        // Assert
        chunks.Should().Contain(c => c.SemanticType == "record" && c.SemanticName == "Person");
    }
}
