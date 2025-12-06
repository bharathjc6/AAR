using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using AAR.Application.Interfaces;
using AAR.Domain.Entities;
using AAR.Domain.Enums;
using AAR.Domain.ValueObjects;
using Microsoft.Extensions.Logging;

namespace AAR.Worker.Agents;

/// <summary>
/// Analyzes code quality including complexity, duplication, and best practices.
/// </summary>
public class CodeQualityAgent : BaseAgent
{
    public override AgentType AgentType => AgentType.CodeQuality;

    public CodeQualityAgent(
        IOpenAiService openAiService,
        ICodeMetricsService metricsService,
        ILogger<CodeQualityAgent> logger)
        : base(openAiService, metricsService, logger)
    {
    }

    public override async Task<List<ReviewFinding>> AnalyzeAsync(
        Guid projectId,
        string workingDirectory,
        CancellationToken cancellationToken = default)
    {
        Logger.LogInformation("CodeQualityAgent analyzing project {ProjectId}", projectId);
        
        var findings = new List<ReviewFinding>();
        var sourceFiles = GetSourceFiles(workingDirectory).ToList();
        
        Logger.LogDebug("Found {Count} source files to analyze", sourceFiles.Count);
        
        try
        {
            // Analyze metrics for all files
            foreach (var file in sourceFiles)
            {
                var metrics = await MetricsService.CalculateMetricsAsync(file, cancellationToken);
                var relativePath = Path.GetRelativePath(workingDirectory, file);
                
                // Check for high complexity
                if (metrics.CyclomaticComplexity > 20)
                {
                    findings.Add(CreateFinding(
                        projectId,
                        "High Cyclomatic Complexity",
                        $"File has cyclomatic complexity of {metrics.CyclomaticComplexity}. Complex code is harder to test and maintain.",
                        metrics.CyclomaticComplexity > 30 ? Severity.High : Severity.Medium,
                        FindingCategory.Complexity,
                        filePath: relativePath,
                        suggestion: "Consider breaking down complex methods into smaller, focused functions."
                    ));
                }
                
                // Check for long files
                if (metrics.LinesOfCode > 500)
                {
                    findings.Add(CreateFinding(
                        projectId,
                        "Long File",
                        $"File has {metrics.LinesOfCode} lines of code. Long files are harder to navigate and maintain.",
                        metrics.LinesOfCode > 1000 ? Severity.Medium : Severity.Low,
                        FindingCategory.Maintainability,
                        filePath: relativePath,
                        suggestion: "Consider splitting this file into multiple smaller, focused files."
                    ));
                }
                
                // Check for too many methods/functions
                if (metrics.MethodCount > 20)
                {
                    findings.Add(CreateFinding(
                        projectId,
                        "Too Many Methods",
                        $"File contains {metrics.MethodCount} methods/functions. This may indicate the class is doing too much.",
                        Severity.Medium,
                        FindingCategory.Maintainability,
                        filePath: relativePath,
                        suggestion: "Consider applying the Single Responsibility Principle and splitting into multiple classes."
                    ));
                }
            }
            
            // Sample files for AI analysis
            var filesToAnalyze = sourceFiles
                .OrderByDescending(f => new FileInfo(f).Length)
                .Take(10)
                .ToList();
            
            foreach (var file in filesToAnalyze)
            {
                if (cancellationToken.IsCancellationRequested) break;
                
                var content = await ReadFileContentAsync(file);
                var relativePath = Path.GetRelativePath(workingDirectory, file);
                
                var aiFindings = await AnalyzeFileWithAiAsync(projectId, relativePath, content, cancellationToken);
                findings.AddRange(aiFindings);
            }
            
            // Additional rule-based checks
            findings.AddRange(await CheckForCommonIssuesAsync(projectId, sourceFiles, workingDirectory, cancellationToken));
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error in CodeQualityAgent analysis");
            findings.Add(CreateFinding(
                projectId,
                "Analysis Error",
                $"Code quality analysis encountered an error: {ex.Message}",
                Severity.Info,
                FindingCategory.CodeQuality
            ));
        }
        
        Logger.LogInformation("CodeQualityAgent found {Count} findings", findings.Count);
        return findings;
    }

    private async Task<List<ReviewFinding>> AnalyzeFileWithAiAsync(
        Guid projectId,
        string relativePath,
        string content,
        CancellationToken cancellationToken)
    {
        var findings = new List<ReviewFinding>();
        
        var prompt = $@"Analyze the following code file for quality issues.
Focus on:
1. Code smells (long methods, god classes, etc.)
2. Naming conventions
3. Error handling
4. Code duplication patterns
5. Performance anti-patterns
6. Best practices violations

File: {relativePath}

```
{content}
```

Respond with a JSON array of findings (max 5 most important):
[
  {{
    ""title"": ""Issue title"",
    ""description"": ""Detailed description"",
    ""severity"": ""Critical|High|Medium|Low|Info"",
    ""lineNumber"": 123,
    ""suggestion"": ""How to fix"",
    ""codeSnippet"": ""relevant code""
  }}
]

Only respond with the JSON array, no other text. If no issues found, respond with [].";

        try
        {
            var response = await OpenAiService.AnalyzeCodeAsync(prompt, "CodeQualityAgent", cancellationToken);
            
            var jsonStart = response.IndexOf('[');
            var jsonEnd = response.LastIndexOf(']');
            
            if (jsonStart >= 0 && jsonEnd > jsonStart)
            {
                var json = response.Substring(jsonStart, jsonEnd - jsonStart + 1);
                var parsed = JsonSerializer.Deserialize<List<AiFinding>>(json, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });
                
                if (parsed != null)
                {
                    foreach (var f in parsed)
                    {
                        var severity = Enum.TryParse<Severity>(f.Severity, ignoreCase: true, out var s)
                            ? s : Severity.Info;
                        
                        LineRange? lineRange = f.LineNumber > 0 
                            ? new LineRange(f.LineNumber, f.LineNumber) 
                            : null;
                        
                        findings.Add(CreateFinding(
                            projectId,
                            f.Title ?? "Code Quality Issue",
                            f.Description ?? "",
                            severity,
                            FindingCategory.CodeQuality,
                            filePath: relativePath,
                            lineRange: lineRange,
                            codeSnippet: f.CodeSnippet,
                            suggestion: f.Suggestion
                        ));
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Failed to analyze file {File} with AI", relativePath);
        }
        
        return findings;
    }

    private async Task<List<ReviewFinding>> CheckForCommonIssuesAsync(
        Guid projectId,
        List<string> sourceFiles,
        string workingDirectory,
        CancellationToken cancellationToken)
    {
        var findings = new List<ReviewFinding>();
        
        foreach (var file in sourceFiles)
        {
            if (cancellationToken.IsCancellationRequested) break;
            
            var content = await File.ReadAllTextAsync(file, cancellationToken);
            var relativePath = Path.GetRelativePath(workingDirectory, file);
            var lines = content.Split('\n');
            
            // Check for TODO/FIXME comments
            for (int i = 0; i < lines.Length; i++)
            {
                var line = lines[i];
                
                if (Regex.IsMatch(line, @"\b(TODO|FIXME|HACK|XXX)\b", RegexOptions.IgnoreCase))
                {
                    findings.Add(CreateFinding(
                        projectId,
                        "Technical Debt Marker",
                        $"Found TODO/FIXME comment: {line.Trim()}",
                        Severity.Info,
                        FindingCategory.Maintainability,
                        filePath: relativePath,
                        lineRange: new LineRange(i + 1, i + 1),
                        codeSnippet: line.Trim()
                    ));
                }
            }
            
            // Check for empty catch blocks
            var emptyCatchPattern = new Regex(@"catch\s*\([^)]*\)\s*\{\s*\}", RegexOptions.Multiline);
            var matches = emptyCatchPattern.Matches(content);
            
            foreach (Match match in matches)
            {
                var lineNumber = content.Substring(0, match.Index).Count(c => c == '\n') + 1;
                
                findings.Add(CreateFinding(
                    projectId,
                    "Empty Catch Block",
                    "Empty catch blocks silently swallow exceptions, hiding potential bugs.",
                    Severity.High,
                    FindingCategory.CodeQuality,
                    filePath: relativePath,
                    lineRange: new LineRange(lineNumber, lineNumber),
                    codeSnippet: match.Value,
                    suggestion: "Log the exception or handle it appropriately. If intentionally ignored, add a comment explaining why."
                ));
            }
            
            // Check for magic numbers
            var magicNumberPattern = new Regex(@"(?<![\w.])\b(?!0|1|-1)\d{2,}\b(?![ulfdULFD])", RegexOptions.Multiline);
            var magicNumbers = magicNumberPattern.Matches(content);
            
            if (magicNumbers.Count > 5)
            {
                findings.Add(CreateFinding(
                    projectId,
                    "Magic Numbers Detected",
                    $"Found {magicNumbers.Count} potential magic numbers. These make code harder to understand and maintain.",
                    Severity.Low,
                    FindingCategory.Maintainability,
                    filePath: relativePath,
                    suggestion: "Extract magic numbers into named constants with descriptive names."
                ));
            }
        }
        
        // Limit findings to prevent overwhelming output
        return findings.Take(50).ToList();
    }

    private class AiFinding
    {
        public string? Title { get; set; }
        public string? Description { get; set; }
        public string? Severity { get; set; }
        public int LineNumber { get; set; }
        public string? Suggestion { get; set; }
        public string? CodeSnippet { get; set; }
    }
}
