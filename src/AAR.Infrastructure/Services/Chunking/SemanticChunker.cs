// =============================================================================
// AAR.Infrastructure - Services/Chunking/SemanticChunker.cs
// Semantic code chunker with Roslyn support for C#
// =============================================================================

using System.Security.Cryptography;
using System.Text;
using AAR.Application.Interfaces;
using AAR.Shared.Tokenization;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AAR.Infrastructure.Services.Chunking;

/// <summary>
/// Semantic chunker that uses Roslyn for C# files and sliding window for others.
/// </summary>
public class SemanticChunker : IChunker
{
    private readonly ITokenizer _tokenizer;
    private readonly ChunkerOptions _options;
    private readonly ILogger<SemanticChunker> _logger;

    private static readonly HashSet<string> CSharpExtensions = [".cs"];
    private static readonly HashSet<string> TextExtensions = [".md", ".txt", ".json", ".xml", ".yaml", ".yml"];

    public SemanticChunker(
        ITokenizerFactory tokenizerFactory,
        IOptions<ChunkerOptions> options,
        ILogger<SemanticChunker> logger)
    {
        _tokenizer = tokenizerFactory.Create();
        _options = options.Value;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<ChunkInfo>> ChunkFileAsync(
        string filePath,
        string content,
        Guid projectId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(content))
            return [];

        var extension = Path.GetExtension(filePath).ToLowerInvariant();
        var language = DetectLanguage(extension);

        _logger.LogDebug("Chunking file {FilePath} ({Language})", filePath, language);

        IReadOnlyList<ChunkInfo> chunks;

        if (_options.UseSemanticSplitting && CSharpExtensions.Contains(extension))
        {
            chunks = await ChunkCSharpFileAsync(filePath, content, projectId, cancellationToken);
        }
        else
        {
            chunks = ChunkWithSlidingWindow(filePath, content, projectId, language);
        }

        _logger.LogDebug("Created {ChunkCount} chunks for {FilePath}", chunks.Count, filePath);
        return chunks;
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<ChunkInfo>> ChunkFilesAsync(
        IDictionary<string, string> files,
        Guid projectId,
        CancellationToken cancellationToken = default)
    {
        var allChunks = new List<ChunkInfo>();

        foreach (var (filePath, content) in files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            
            var chunks = await ChunkFileAsync(filePath, content, projectId, cancellationToken);
            allChunks.AddRange(chunks);
        }

        _logger.LogInformation("Created {TotalChunks} chunks from {FileCount} files", 
            allChunks.Count, files.Count);
        
        return allChunks;
    }

    private async Task<IReadOnlyList<ChunkInfo>> ChunkCSharpFileAsync(
        string filePath,
        string content,
        Guid projectId,
        CancellationToken cancellationToken)
    {
        var chunks = new List<ChunkInfo>();

        try
        {
            var tree = CSharpSyntaxTree.ParseText(content, cancellationToken: cancellationToken);
            var root = await tree.GetRootAsync(cancellationToken);
            var lines = content.Split('\n');

            // Extract semantic units
            var semanticUnits = ExtractSemanticUnits(root, lines);

            foreach (var unit in semanticUnits)
            {
                var tokenCount = _tokenizer.CountTokens(unit.Content);

                if (tokenCount > _options.MaxChunkTokens)
                {
                    // Split large units into smaller chunks with sliding window
                    var subChunks = ChunkWithSlidingWindow(
                        filePath, 
                        unit.Content, 
                        projectId, 
                        "csharp",
                        unit.StartLine,
                        unit.SemanticType,
                        unit.SemanticName);
                    chunks.AddRange(subChunks);
                }
                else if (tokenCount >= _options.MinChunkTokens)
                {
                    chunks.Add(CreateChunkInfo(
                        filePath,
                        unit.Content,
                        projectId,
                        unit.StartLine,
                        unit.EndLine,
                        tokenCount,
                        "csharp",
                        unit.SemanticType,
                        unit.SemanticName));
                }
            }

            // If no semantic units found, fall back to sliding window
            if (chunks.Count == 0)
            {
                chunks.AddRange(ChunkWithSlidingWindow(filePath, content, projectId, "csharp"));
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to parse C# file {FilePath}, using sliding window", filePath);
            chunks.AddRange(ChunkWithSlidingWindow(filePath, content, projectId, "csharp"));
        }

        return chunks;
    }

    private List<SemanticUnit> ExtractSemanticUnits(SyntaxNode root, string[] lines)
    {
        var units = new List<SemanticUnit>();

        // Extract classes, structs, records, interfaces
        var typeDeclarations = root.DescendantNodes()
            .OfType<TypeDeclarationSyntax>();

        foreach (var type in typeDeclarations)
        {
            var span = type.GetLocation().GetLineSpan();
            var startLine = span.StartLinePosition.Line + 1;
            var endLine = span.EndLinePosition.Line + 1;
            var content = GetLinesContent(lines, startLine, endLine);

            // Check if the type is small enough to be a single chunk
            var tokenCount = _tokenizer.CountTokens(content);
            
            if (tokenCount <= _options.MaxChunkTokens)
            {
                units.Add(new SemanticUnit
                {
                    Content = content,
                    StartLine = startLine,
                    EndLine = endLine,
                    SemanticType = GetTypeName(type),
                    SemanticName = type.Identifier.Text
                });
            }
            else
            {
                // Break down into members
                ExtractMemberUnits(type, lines, units);
            }
        }

        // If no type declarations, try top-level statements
        if (units.Count == 0)
        {
            var globalStatements = root.DescendantNodes()
                .OfType<GlobalStatementSyntax>()
                .ToList();

            if (globalStatements.Count > 0)
            {
                var first = globalStatements.First();
                var last = globalStatements.Last();
                var startLine = first.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
                var endLine = last.GetLocation().GetLineSpan().EndLinePosition.Line + 1;
                var content = GetLinesContent(lines, startLine, endLine);

                units.Add(new SemanticUnit
                {
                    Content = content,
                    StartLine = startLine,
                    EndLine = endLine,
                    SemanticType = "top-level",
                    SemanticName = "Program"
                });
            }
        }

        return units;
    }

    private void ExtractMemberUnits(TypeDeclarationSyntax type, string[] lines, List<SemanticUnit> units)
    {
        foreach (var member in type.Members)
        {
            var span = member.GetLocation().GetLineSpan();
            var startLine = span.StartLinePosition.Line + 1;
            var endLine = span.EndLinePosition.Line + 1;
            var content = GetLinesContent(lines, startLine, endLine);

            var (semanticType, semanticName) = member switch
            {
                MethodDeclarationSyntax m => ("method", m.Identifier.Text),
                PropertyDeclarationSyntax p => ("property", p.Identifier.Text),
                FieldDeclarationSyntax f => ("field", f.Declaration.Variables.FirstOrDefault()?.Identifier.Text ?? "field"),
                ConstructorDeclarationSyntax c => ("constructor", c.Identifier.Text),
                EventDeclarationSyntax e => ("event", e.Identifier.Text),
                IndexerDeclarationSyntax => ("indexer", "this"),
                OperatorDeclarationSyntax o => ("operator", o.OperatorToken.Text),
                _ => ("member", "unknown")
            };

            units.Add(new SemanticUnit
            {
                Content = content,
                StartLine = startLine,
                EndLine = endLine,
                SemanticType = semanticType,
                SemanticName = semanticName
            });
        }
    }

    private static string GetTypeName(TypeDeclarationSyntax type) => type switch
    {
        ClassDeclarationSyntax => "class",
        StructDeclarationSyntax => "struct",
        RecordDeclarationSyntax r => r.ClassOrStructKeyword.IsKind(SyntaxKind.StructKeyword) ? "record struct" : "record",
        InterfaceDeclarationSyntax => "interface",
        _ => "type"
    };

    private IReadOnlyList<ChunkInfo> ChunkWithSlidingWindow(
        string filePath,
        string content,
        Guid projectId,
        string language,
        int baseLineOffset = 1,
        string? parentSemanticType = null,
        string? parentSemanticName = null)
    {
        var chunks = new List<ChunkInfo>();
        var lines = content.Split('\n');
        var currentChunk = new StringBuilder();
        var chunkStartLine = baseLineOffset;
        var currentLineIndex = 0;

        while (currentLineIndex < lines.Length)
        {
            currentChunk.Clear();
            chunkStartLine = baseLineOffset + currentLineIndex;

            // Build chunk up to max tokens
            while (currentLineIndex < lines.Length)
            {
                var testContent = currentChunk.Length > 0 
                    ? currentChunk + "\n" + lines[currentLineIndex]
                    : lines[currentLineIndex];

                var tokenCount = _tokenizer.CountTokens(testContent);

                if (tokenCount > _options.MaxChunkTokens && currentChunk.Length > 0)
                {
                    break;
                }

                if (currentChunk.Length > 0)
                    currentChunk.AppendLine();
                currentChunk.Append(lines[currentLineIndex]);
                currentLineIndex++;
            }

            var chunkContent = currentChunk.ToString();
            var chunkTokenCount = _tokenizer.CountTokens(chunkContent);
            var chunkEndLine = baseLineOffset + currentLineIndex - 1;

            if (chunkTokenCount >= _options.MinChunkTokens)
            {
                chunks.Add(CreateChunkInfo(
                    filePath,
                    chunkContent,
                    projectId,
                    chunkStartLine,
                    chunkEndLine,
                    chunkTokenCount,
                    language,
                    parentSemanticType,
                    parentSemanticName));
            }

            // Apply overlap by going back
            if (currentLineIndex < lines.Length)
            {
                var overlapTokens = 0;
                var overlapLines = 0;

                while (overlapLines < currentLineIndex - (chunkStartLine - baseLineOffset) && 
                       overlapTokens < _options.OverlapTokens)
                {
                    overlapLines++;
                    var lineIndex = currentLineIndex - overlapLines;
                    if (lineIndex >= 0 && lineIndex < lines.Length)
                    {
                        overlapTokens += _tokenizer.CountTokens(lines[lineIndex]);
                    }
                }

                currentLineIndex -= overlapLines;
            }
        }

        return chunks;
    }

    private ChunkInfo CreateChunkInfo(
        string filePath,
        string content,
        Guid projectId,
        int startLine,
        int endLine,
        int tokenCount,
        string language,
        string? semanticType = null,
        string? semanticName = null)
    {
        var textHash = ComputeHash(content);
        var chunkHash = ComputeHash($"{filePath}:{startLine}:{endLine}:{textHash}");

        return new ChunkInfo
        {
            ChunkHash = chunkHash,
            ProjectId = projectId,
            FilePath = filePath,
            StartLine = startLine,
            EndLine = endLine,
            TokenCount = tokenCount,
            Language = language,
            TextHash = textHash,
            Content = content,
            SemanticType = semanticType,
            SemanticName = semanticName
        };
    }

    private static string GetLinesContent(string[] lines, int startLine, int endLine)
    {
        // Convert 1-based lines to 0-based index
        var start = Math.Max(0, startLine - 1);
        var end = Math.Min(lines.Length - 1, endLine - 1);
        
        return string.Join('\n', lines.Skip(start).Take(end - start + 1));
    }

    private static string DetectLanguage(string extension) => extension switch
    {
        ".cs" => "csharp",
        ".ts" or ".tsx" => "typescript",
        ".js" or ".jsx" => "javascript",
        ".py" => "python",
        ".java" => "java",
        ".go" => "go",
        ".rs" => "rust",
        ".cpp" or ".cc" or ".cxx" => "cpp",
        ".c" => "c",
        ".h" or ".hpp" => "c-header",
        ".md" => "markdown",
        ".json" => "json",
        ".xml" => "xml",
        ".yaml" or ".yml" => "yaml",
        _ => "text"
    };

    private static string ComputeHash(string input)
    {
        var bytes = Encoding.UTF8.GetBytes(input);
        var hashBytes = SHA256.HashData(bytes);
        return Convert.ToHexStringLower(hashBytes)[..16]; // Use first 16 chars for brevity
    }

    private record SemanticUnit
    {
        public required string Content { get; init; }
        public required int StartLine { get; init; }
        public required int EndLine { get; init; }
        public string? SemanticType { get; init; }
        public string? SemanticName { get; init; }
    }
}
