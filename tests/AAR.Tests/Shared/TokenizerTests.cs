// =============================================================================
// AAR.Tests - Tokenization/TokenizerTests.cs
// Unit tests for tokenizer implementations
// =============================================================================

using AAR.Shared.Tokenization;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace AAR.Tests.Shared;

public class TokenizerTests
{
    [Fact]
    public void HeuristicTokenizer_CountTokens_ReturnsApproximateCount()
    {
        // Arrange
        var tokenizer = new HeuristicTokenizer();
        var text = "Hello, World!"; // 13 chars

        // Act
        var count = tokenizer.CountTokens(text);

        // Assert
        // Heuristic uses ~4 chars per token
        count.Should().Be(4); // 13 / 4 = 3.25, rounded up = 4
    }

    [Fact]
    public void HeuristicTokenizer_CountTokens_WithEmptyString_ReturnsZero()
    {
        // Arrange
        var tokenizer = new HeuristicTokenizer();

        // Act
        var count = tokenizer.CountTokens("");

        // Assert
        count.Should().Be(0);
    }

    [Fact]
    public void HeuristicTokenizer_CountTokens_WithNull_ReturnsZero()
    {
        // Arrange
        var tokenizer = new HeuristicTokenizer();

        // Act
        var count = tokenizer.CountTokens(null!);

        // Assert
        count.Should().Be(0);
    }

    [Fact]
    public void HeuristicTokenizer_IsHeuristic_ReturnsTrue()
    {
        // Arrange
        var tokenizer = new HeuristicTokenizer();

        // Assert
        tokenizer.IsHeuristic.Should().BeTrue();
    }

    [Fact]
    public void TiktokenTokenizer_CountTokens_ReturnsAccurateCount()
    {
        // Arrange
        var options = Options.Create(new TokenizerOptions { Encoding = "gpt-4o" });
        using var tokenizer = new TiktokenTokenizer(options);
        var text = "Hello, World!";

        // Act
        var count = tokenizer.CountTokens(text);

        // Assert
        // "Hello, World!" is typically 4 tokens in GPT tokenization
        count.Should().BeGreaterThan(0);
        count.Should().BeLessThanOrEqualTo(10); // Reasonable range
    }

    [Fact]
    public void TiktokenTokenizer_IsHeuristic_ReturnsFalse()
    {
        // Arrange
        var options = Options.Create(new TokenizerOptions { Encoding = "gpt-4o" });
        using var tokenizer = new TiktokenTokenizer(options);

        // Assert
        tokenizer.IsHeuristic.Should().BeFalse();
    }

    [Fact]
    public void TiktokenTokenizer_EncodeAndDecode_RoundTrips()
    {
        // Arrange
        var options = Options.Create(new TokenizerOptions { Encoding = "gpt-4o" });
        using var tokenizer = new TiktokenTokenizer(options);
        var originalText = "Hello, World!";

        // Act
        var tokens = tokenizer.Encode(originalText);
        var decoded = tokenizer.Decode(tokens);

        // Assert
        decoded.Should().Be(originalText);
    }

    [Fact]
    public void TiktokenTokenizer_CountTokens_WithCodeBlock_CountsCorrectly()
    {
        // Arrange
        var options = Options.Create(new TokenizerOptions { Encoding = "gpt-4o" });
        using var tokenizer = new TiktokenTokenizer(options);
        var code = @"
public class Example
{
    public void Method()
    {
        Console.WriteLine(""Hello"");
    }
}";

        // Act
        var count = tokenizer.CountTokens(code);

        // Assert
        count.Should().BeGreaterThan(10); // Code has significant tokens
        count.Should().BeLessThan(100); // But not too many for this small snippet
    }

    [Fact]
    public void TokenizerFactory_Create_ReturnsTiktokenByDefault()
    {
        // Arrange
        var options = Options.Create(new TokenizerOptions { Mode = TokenizerMode.Tiktoken });
        var loggerFactory = new Mock<ILoggerFactory>();
        loggerFactory.Setup(f => f.CreateLogger(It.IsAny<string>())).Returns(Mock.Of<ILogger>());
        
        var factory = new TokenizerFactory(options, loggerFactory.Object);

        // Act
        var tokenizer = factory.Create();

        // Assert
        tokenizer.Should().BeOfType<TiktokenTokenizer>();
        tokenizer.IsHeuristic.Should().BeFalse();
    }

    [Fact]
    public void TokenizerFactory_Create_ReturnsHeuristicWhenConfigured()
    {
        // Arrange
        var options = Options.Create(new TokenizerOptions { Mode = TokenizerMode.Heuristic });
        var loggerFactory = new Mock<ILoggerFactory>();
        loggerFactory.Setup(f => f.CreateLogger(It.IsAny<string>())).Returns(Mock.Of<ILogger>());
        
        var factory = new TokenizerFactory(options, loggerFactory.Object);

        // Act
        var tokenizer = factory.Create();

        // Assert
        tokenizer.Should().BeOfType<HeuristicTokenizer>();
        tokenizer.IsHeuristic.Should().BeTrue();
    }
}
