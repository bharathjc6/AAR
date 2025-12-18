// =============================================================================
// AAR.Infrastructure - Services/Chunking/SemanticChunker.cs
// Semantic code chunker with Roslyn support for C#
// =============================================================================

using System.Security.Cryptography;
using System.Text;
using System.IO;
using System.Linq;
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

    private static readonly HashSet<string> CSharpExtensions = new(StringComparer.OrdinalIgnoreCase) { ".cs" };
    private static readonly HashSet<string> TextExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".md", ".txt", ".json", ".xml", ".yaml", ".yml"
    };

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
        {
            _logger.LogWarning("Chunker: skipping empty or whitespace-only file {FilePath}", filePath);
            return Array.Empty<ChunkInfo>();
        }

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
            // Use language-aware heuristics to extract semantic units for non-C# languages
            var lines = content.Split('\n');
            var units = ExtractSemanticUnitsForLanguage(language, content, lines, filePath);

            var allChunks = new List<ChunkInfo>();
            foreach (var unit in units)
            {
                var tokenCount = _tokenizer.CountTokens(unit.Content);
                if (tokenCount > _options.MaxChunkTokens)
                {
                    var subChunks = ChunkWithSlidingWindow(
                        filePath,
                        unit.Content,
                        projectId,
                        language,
                        unit.StartLine,
                        unit.SemanticType,
                        unit.SemanticName,
                        unit.Namespace);
                    allChunks.AddRange(subChunks);
                }
                else if (tokenCount >= _options.MinChunkTokens)
                {
                    var c = CreateChunkInfo(
                        filePath,
                        unit.Content,
                        projectId,
                        unit.StartLine,
                        unit.EndLine,
                        tokenCount,
                        language,
                        unit.SemanticType,
                        unit.SemanticName,
                        unit.Namespace);

                    // Ensure semantic metadata never null
                    if (string.IsNullOrWhiteSpace(c.SemanticType) || string.IsNullOrWhiteSpace(c.SemanticName))
                    {
                        var fallbackName = Path.GetFileName(filePath);
                        c = c with { SemanticType = "file", SemanticName = fallbackName };
                    }

                    allChunks.Add(c);
                    _logger.LogDebug("Created single chunk for semantic unit {Semantic} total_chunks=1 chunk_index=0", c.SemanticName);
                }
            }

            // If extraction produced no units, fallback to whole-file chunking
            if (allChunks.Count == 0)
            {
                chunks = ChunkWithSlidingWindow(filePath, content, projectId, language);
            }
            else
            {
                chunks = allChunks;
            }
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
            _logger.LogDebug("Chunker: discovered file {FilePath} (size: {Size} chars)", filePath, content?.Length ?? 0);

            if (string.IsNullOrWhiteSpace(content))
            {
                _logger.LogDebug("Chunker: skipping file {FilePath} due to empty content", filePath);
                continue;
            }

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
            // Add timeout for Roslyn parsing to prevent hanging on complex files
            using var parseTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            parseTimeout.CancelAfter(TimeSpan.FromSeconds(30)); // 30 second timeout per file
            
            var tree = CSharpSyntaxTree.ParseText(content, cancellationToken: parseTimeout.Token);
            var root = await tree.GetRootAsync(parseTimeout.Token);
            var lines = content.Split('\n');

            // Extract semantic units (synchronous, so wrap with Task.Run for timeout)
            List<SemanticUnit> semanticUnits;
            try
            {
                semanticUnits = await Task.Run(() => ExtractSemanticUnits(root, lines), parseTimeout.Token);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                _logger.LogWarning("Roslyn parsing timed out for {FilePath}, using sliding window", filePath);
                return ChunkWithSlidingWindow(filePath, content, projectId, "csharp");
            }

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
                        unit.SemanticName,
                        unit.Namespace);
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
                // Attempt to find the namespace that owns this type
                var nsNode = type.Ancestors().OfType<NamespaceDeclarationSyntax>().FirstOrDefault()
                             ?? (SyntaxNode?)type.Ancestors().OfType<FileScopedNamespaceDeclarationSyntax>().FirstOrDefault();
                string? ns = null;
                if (nsNode is NamespaceDeclarationSyntax nd)
                    ns = nd.Name.ToString();
                else if (nsNode is FileScopedNamespaceDeclarationSyntax fs)
                    ns = fs.Name.ToString();

                units.Add(new SemanticUnit
                {
                    Content = content,
                    StartLine = startLine,
                    EndLine = endLine,
                    SemanticType = GetTypeName(type),
                    SemanticName = type.Identifier.Text,
                    Namespace = ns
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

    private List<SemanticUnit> ExtractSemanticUnitsForLanguage(string language, string content, string[] lines, string filePath)
    {
        var units = new List<SemanticUnit>();
        try
        {
            if (language == "java")
            {
                // Extract methods and classes via brace parsing
                var methodRegex = new System.Text.RegularExpressions.Regex(@"\b([A-Za-z_][A-Za-z0-9_]*)\s*\([^\)]*\)\s*\{", System.Text.RegularExpressions.RegexOptions.Compiled);
                foreach (System.Text.RegularExpressions.Match m in methodRegex.Matches(content))
                {
                    var name = m.Groups[1].Value;
                    var start = m.Index;
                    var (endIndex, endLine) = FindMatchingBrace(content, start);
                    var startLine = content.Take(start).Count(c => c == '\n') + 1;
                    var unitContent = content.Substring(start, Math.Max(0, endIndex - start + 1));
                    units.Add(new SemanticUnit { Content = unitContent, StartLine = startLine, EndLine = endLine, SemanticType = "method", SemanticName = name });
                }

                if (units.Count == 0)
                {
                    // find top-level class
                    var classRegex = new System.Text.RegularExpressions.Regex(@"\bclass\s+([A-Za-z_][A-Za-z0-9_]*)", System.Text.RegularExpressions.RegexOptions.Compiled);
                    var cm = classRegex.Match(content);
                    if (cm.Success)
                    {
                        var name = cm.Groups[1].Value;
                        units.Add(new SemanticUnit { Content = content, StartLine = 1, EndLine = lines.Length, SemanticType = "class", SemanticName = name });
                    }
                }
            }
            else if (language == "typescript" || language == "javascript")
            {
                var funcRegex = new System.Text.RegularExpressions.Regex(@"function\s+([A-Za-z_][A-Za-z0-9_]*)\s*\(|([A-Za-z_][A-Za-z0-9_]*)\s*=\s*\([^\)]*\)\s*=>", System.Text.RegularExpressions.RegexOptions.Compiled);
                foreach (System.Text.RegularExpressions.Match m in funcRegex.Matches(content))
                {
                    var name = m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Value;
                    var start = m.Index;
                    var (endIndex, endLine) = FindMatchingBrace(content, start);
                    var startLine = content.Take(start).Count(c => c == '\n') + 1;
                    var unitContent = content.Substring(start, Math.Max(0, endIndex - start + 1));
                    units.Add(new SemanticUnit { Content = unitContent, StartLine = startLine, EndLine = endLine, SemanticType = "method", SemanticName = name });
                }
            }
            else if (language == "python")
            {
                var linesList = lines.ToList();
                for (int i = 0; i < linesList.Count; i++)
                {
                    var line = linesList[i];
                    var m = System.Text.RegularExpressions.Regex.Match(line, "^\\s*def\\s+([A-Za-z_][A-Za-z0-9_]*)\\s*\\(");
                    if (m.Success)
                    {
                        var name = m.Groups[1].Value;
                        var startLine = i + 1;
                        int j = i + 1;
                        while (j < linesList.Count && !System.Text.RegularExpressions.Regex.IsMatch(linesList[j], "^\\s*(def |class )")) j++;
                        var endLine = Math.Max(startLine, j);
                        var unitContent = string.Join('\n', linesList.Skip(i).Take(endLine - i));
                        units.Add(new SemanticUnit { Content = unitContent, StartLine = startLine, EndLine = endLine, SemanticType = "method", SemanticName = name });
                    }
                }
                if (units.Count == 0)
                {
                    // fallback to classes
                    var classRegex = new System.Text.RegularExpressions.Regex(@"^\s*class\s+([A-Za-z_][A-Za-z0-9_]*)", System.Text.RegularExpressions.RegexOptions.Multiline);
                    var cm = classRegex.Match(content);
                    if (cm.Success)
                    {
                        var name = cm.Groups[1].Value;
                        units.Add(new SemanticUnit { Content = content, StartLine = 1, EndLine = lines.Length, SemanticType = "class", SemanticName = name });
                    }
                }
            }

            // Normalize results and apply fallback per-file if none found
            if (units.Count == 0)
            {
                units.Add(new SemanticUnit { Content = content, StartLine = 1, EndLine = lines.Length, SemanticType = "file", SemanticName = Path.GetFileName(filePath) });
            }

            // Ensure semantic fields populated
            foreach (var u in units)
            {
                if (string.IsNullOrWhiteSpace(u.SemanticType)) u.SemanticType = "file";
                if (string.IsNullOrWhiteSpace(u.SemanticName)) u.SemanticName = Path.GetFileName(filePath);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Heuristic semantic extraction failed for {File}, falling back to file-level unit", filePath);
            units.Clear();
            units.Add(new SemanticUnit { Content = content, StartLine = 1, EndLine = lines.Length, SemanticType = "file", SemanticName = Path.GetFileName(filePath) });
        }

        return units;
    }

    private (int endIndex, int endLine) FindMatchingBrace(string content, int startIndex)
    {
        int i = content.IndexOf('{', startIndex);
        if (i < 0) return (Math.Min(content.Length - 1, startIndex + 200), content.Take(Math.Min(content.Length, startIndex + 200)).Count(c => c == '\n') + 1);
        int depth = 0;
        for (int j = i; j < content.Length; j++)
        {
            if (content[j] == '{') depth++;
            else if (content[j] == '}') depth--;
            if (depth == 0)
            {
                var endLine = content.Take(j).Count(c => c == '\n') + 1;
                return (j, endLine);
            }
        }
        // fallback to file end
        return (content.Length - 1, content.Count(c => c == '\n') + 1);
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
                SemanticName = semanticName,
                Namespace = type.FirstAncestorOrSelf<NamespaceDeclarationSyntax>()?.Name.ToString()
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
        string? parentSemanticName = null,
        string? parentNamespace = null)
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
                    parentSemanticName,
                    parentNamespace));
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

        // Ensure every call produces at least one chunk for the provided semantic unit/file
        if (chunks.Count == 0)
        {
            var fullContent = content;
            var fullTokenCount = _tokenizer.CountTokens(fullContent);
            // create a single chunk even if it is below MinChunkTokens to preserve semantic unit
            chunks.Add(CreateChunkInfo(
                filePath,
                fullContent,
                projectId,
                baseLineOffset,
                baseLineOffset + Math.Max(0, lines.Length - 1),
                fullTokenCount,
                language,
                parentSemanticType,
                parentSemanticName,
                parentNamespace));
        }

        // Assign chunk indices and total_chunks for this semantic unit
        var total = chunks.Count;
        if (total <= 0)
        {
            throw new InvalidOperationException($"Invalid chunk count ({total}) generated for semantic unit in file {filePath}");
        }

        for (int i = 0; i < total; i++)
        {
            var c = chunks[i];
            chunks[i] = c with { ChunkIndex = i, TotalChunks = total };
        }

        // Log the assigned range for caller visibility
        _logger.LogDebug("Chunking produced {Total} chunks for {Semantic} (lines {StartLine}-{EndLine}), chunk_index range: 0..{MaxIndex}", total, parentSemanticName ?? Path.GetFileName(filePath), baseLineOffset, baseLineOffset + lines.Length - 1, Math.Max(0, total - 1));

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
        string? semanticName = null,
        string? ns = null,
        string? responsibility = null)
    {
        var textHash = ComputeHash(content);
        // Include projectId in hash to ensure uniqueness across projects
        var chunkHash = ComputeHash($"{projectId}:{filePath}:{startLine}:{endLine}:{textHash}");

        // Enforce semantic metadata presence per rules
        var finalSemanticType = string.IsNullOrWhiteSpace(semanticType) ? "file" : semanticType;
        var finalSemanticName = string.IsNullOrWhiteSpace(semanticName) ? Path.GetFileName(filePath) : semanticName;

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
            SemanticType = finalSemanticType,
            SemanticName = finalSemanticName,
            Namespace = ns,
            Responsibility = responsibility
            ,ChunkIndex = 0
            ,TotalChunks = 1
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
        // Use standard API and normalize to lower-case for deterministic short hash
        return Convert.ToHexString(hashBytes).ToLowerInvariant()[..16]; // Use first 16 chars for brevity
    }

    private class SemanticUnit
    {
        public string Content { get; set; } = string.Empty;
        public int StartLine { get; set; }
        public int EndLine { get; set; }
        public string? SemanticType { get; set; }
        public string? SemanticName { get; set; }
        public string? Namespace { get; set; }
    }
}
