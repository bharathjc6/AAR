using System.Text;
using System.Text.Json;
using AAR.Application.Interfaces;
using AAR.Domain.Entities;
using AAR.Domain.Enums;
using AAR.Domain.ValueObjects;
using Microsoft.Extensions.Logging;

namespace AAR.Worker.Agents;

/// <summary>
/// Analyzes the project structure, folder organization, and naming conventions.
/// </summary>
public class StructureAgent : BaseAgent
{
    public override AgentType AgentType => AgentType.Structure;

    public StructureAgent(
        IOpenAiService openAiService,
        ICodeMetricsService metricsService,
        ILogger<StructureAgent> logger) 
        : base(openAiService, metricsService, logger)
    {
    }

    public override async Task<List<ReviewFinding>> AnalyzeAsync(
        Guid projectId,
        string workingDirectory,
        CancellationToken cancellationToken = default)
    {
        Logger.LogInformation("StructureAgent analyzing project {ProjectId}", projectId);
        
        var findings = new List<ReviewFinding>();
        
        try
        {
            // Build directory structure representation
            var structure = BuildDirectoryStructure(workingDirectory);
            
            // Analyze with AI
            var prompt = BuildAnalysisPrompt(structure, workingDirectory);
            var response = await OpenAiService.AnalyzeCodeAsync(prompt, "StructureAgent", cancellationToken);
            
            // Parse AI response into findings
            var aiFindings = ParseAiResponse(projectId, response);
            findings.AddRange(aiFindings);
            
            // Add rule-based findings
            findings.AddRange(AnalyzeNamingConventions(projectId, workingDirectory));
            findings.AddRange(AnalyzeFolderDepth(projectId, workingDirectory));
            findings.AddRange(AnalyzeFileOrganization(projectId, workingDirectory));
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error in StructureAgent analysis");
            findings.Add(CreateFinding(
                projectId,
                "Analysis Error",
                $"Structure analysis encountered an error: {ex.Message}",
                Severity.Info,
                FindingCategory.Structure
            ));
        }
        
        Logger.LogInformation("StructureAgent found {Count} findings", findings.Count);
        return findings;
    }

    private string BuildDirectoryStructure(string rootPath)
    {
        var sb = new StringBuilder();
        BuildTreeRecursive(sb, rootPath, "", 0, maxDepth: 5);
        return sb.ToString();
    }

    private void BuildTreeRecursive(StringBuilder sb, string path, string indent, int depth, int maxDepth)
    {
        if (depth > maxDepth) return;
        
        var dirInfo = new DirectoryInfo(path);
        sb.AppendLine($"{indent}{dirInfo.Name}/");
        
        // Skip excluded directories
        if (IsExcludedPath(path + Path.DirectorySeparatorChar)) return;
        
        var newIndent = indent + "  ";
        
        // Add files (limit per directory)
        var files = dirInfo.GetFiles().Take(20).ToList();
        foreach (var file in files)
        {
            sb.AppendLine($"{newIndent}{file.Name}");
        }
        
        if (dirInfo.GetFiles().Length > 20)
        {
            sb.AppendLine($"{newIndent}... ({dirInfo.GetFiles().Length - 20} more files)");
        }
        
        // Recurse into subdirectories
        foreach (var subDir in dirInfo.GetDirectories())
        {
            if (!IsExcludedPath(subDir.FullName + Path.DirectorySeparatorChar))
            {
                BuildTreeRecursive(sb, subDir.FullName, newIndent, depth + 1, maxDepth);
            }
        }
    }

    private string BuildAnalysisPrompt(string structure, string workingDirectory)
    {
                return $@"Analyze the following project structure for a code repository and provide only evidence-backed findings.
                Each finding MUST include either a `filePath` or a `symbol` and a `confidence` score between 0.0 and 1.0. Do not emit findings without verifiable evidence.

                Project Structure:
                {structure}

                Respond with a JSON array of findings using this schema:
                [
                    {{
                        ""id"": ""unique-id"",
                        ""description"": ""Detailed description of the issue"",
                        ""explanation"": ""Evidence citation including file:line if applicable"",
                        ""severity"": ""Critical|High|Medium|Low|Info"",
                        ""category"": ""Structure"",
                        ""filePath"": ""relative/path/if/applicable"",
                        ""lineRange"": {{ ""start"": 10, ""end"": 12 }},
                        ""symbol"": ""Namespace.Class.Member"",
                        ""confidence"": 0.87,
                        ""suggestedFix"": ""How to fix or improve""
                    }}
                ]

                Only output the JSON array. If no findings, output [].";
    }

    private List<ReviewFinding> ParseAiResponse(Guid projectId, string response)
    {
        var findings = new List<ReviewFinding>();
        
        try
        {
            // Try to extract JSON from response
            var jsonStart = response.IndexOf('[');
            var jsonEnd = response.LastIndexOf(']');
            
            if (jsonStart >= 0 && jsonEnd > jsonStart)
            {
                var json = response.Substring(jsonStart, jsonEnd - jsonStart + 1);
                var parsed = JsonSerializer.Deserialize<List<AiFinding>>(json, AiFindingModels.JsonOptions);
                
                if (parsed != null)
                {
                    foreach (var f in parsed)
                    {
                        var severity = Enum.TryParse<Severity>(f.Severity, ignoreCase: true, out var s) 
                            ? s : Severity.Info;

                        LineRange? lineRange = null;
                        if (f.LineRange is not null && f.LineRange.Start > 0)
                        {
                            lineRange = new LineRange(f.LineRange.Start, f.LineRange.End > 0 ? f.LineRange.End : f.LineRange.Start);
                        }

                        if (!string.IsNullOrWhiteSpace(f.FilePath) || !string.IsNullOrWhiteSpace(f.Symbol))
                        {
                            findings.Add(ReviewFinding.Create(
                                projectId: projectId,
                                reportId: Guid.Empty,
                                agentType: AgentType,
                                category: FindingCategory.Structure,
                                severity: severity,
                                description: f.Title ?? "Structure Finding",
                                explanation: f.Explanation ?? f.Description ?? "",
                                filePath: f.FilePath,
                                fileRecordId: null,
                                lineRange: lineRange,
                                suggestedFix: f.SuggestedFix,
                                fixedCodeSnippet: f.FixedCodeSnippet,
                                originalCodeSnippet: f.OriginalCodeSnippet,
                                symbol: f.Symbol,
                                confidence: f.Confidence)
                            );
                        }
                        else
                        {
                            findings.Add(CreateFinding(
                                projectId,
                                f.Title ?? "Structure Finding",
                                f.Description ?? f.Explanation ?? "",
                                severity,
                                FindingCategory.Structure,
                                suggestion: f.SuggestedFix
                            ));
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Failed to parse AI response for structure analysis");
        }
        
        return findings;
    }

    private IEnumerable<ReviewFinding> AnalyzeNamingConventions(Guid projectId, string workingDirectory)
    {
        var findings = new List<ReviewFinding>();
        
        // Check for inconsistent naming
        var allDirs = Directory.GetDirectories(workingDirectory, "*", SearchOption.AllDirectories)
            .Where(d => !IsExcludedPath(d + Path.DirectorySeparatorChar))
            .Select(d => new DirectoryInfo(d).Name)
            .ToList();
        
        var pascalCase = allDirs.Count(n => char.IsUpper(n[0]) && n.All(c => char.IsLetterOrDigit(c)));
        var kebabCase = allDirs.Count(n => n.Contains('-') && n.All(c => char.IsLower(c) || c == '-' || char.IsDigit(c)));
        var snakeCase = allDirs.Count(n => n.Contains('_') && n.All(c => char.IsLower(c) || c == '_' || char.IsDigit(c)));
        
        if (pascalCase > 0 && kebabCase > 0)
        {
            findings.Add(CreateFinding(
                projectId,
                "Inconsistent Directory Naming",
                $"Mixed naming conventions detected: {pascalCase} PascalCase, {kebabCase} kebab-case directories.",
                Severity.Low,
                FindingCategory.Structure,
                suggestion: "Choose a consistent naming convention for all directories."
            ));
        }
        
        return findings;
    }

    private IEnumerable<ReviewFinding> AnalyzeFolderDepth(Guid projectId, string workingDirectory)
    {
        var findings = new List<ReviewFinding>();
        
        var maxDepth = 0;
        var deepestPath = "";
        
        foreach (var dir in Directory.GetDirectories(workingDirectory, "*", SearchOption.AllDirectories))
        {
            if (IsExcludedPath(dir + Path.DirectorySeparatorChar)) continue;
            
            var relativePath = Path.GetRelativePath(workingDirectory, dir);
            var depth = relativePath.Split(Path.DirectorySeparatorChar).Length;
            
            if (depth > maxDepth)
            {
                maxDepth = depth;
                deepestPath = relativePath;
            }
        }
        
        if (maxDepth > 7)
        {
            findings.Add(CreateFinding(
                projectId,
                "Excessive Folder Nesting",
                $"Directory nesting is {maxDepth} levels deep. This can make navigation difficult.",
                Severity.Medium,
                FindingCategory.Structure,
                filePath: deepestPath,
                suggestion: "Consider flattening the directory structure or using a more modular organization."
            ));
        }
        
        return findings;
    }

    private IEnumerable<ReviewFinding> AnalyzeFileOrganization(Guid projectId, string workingDirectory)
    {
        var findings = new List<ReviewFinding>();
        
        // Check for very large directories
        foreach (var dir in Directory.GetDirectories(workingDirectory, "*", SearchOption.AllDirectories))
        {
            if (IsExcludedPath(dir + Path.DirectorySeparatorChar)) continue;
            
            var fileCount = Directory.GetFiles(dir).Length;
            
            if (fileCount > 50)
            {
                findings.Add(CreateFinding(
                    projectId,
                    "Large Directory",
                    $"Directory contains {fileCount} files, which may indicate poor organization.",
                    Severity.Low,
                    FindingCategory.Structure,
                    filePath: Path.GetRelativePath(workingDirectory, dir),
                    suggestion: "Consider breaking this directory into smaller, more focused subdirectories."
                ));
            }
        }
        
        // Check for missing common directories
        var commonDirs = new[] { "tests", "test", "docs", "documentation" };
        var existingDirs = Directory.GetDirectories(workingDirectory)
            .Select(d => new DirectoryInfo(d).Name.ToLowerInvariant())
            .ToHashSet();
        
        var hasTestDir = existingDirs.Any(d => d.Contains("test"));
        var hasDocDir = existingDirs.Any(d => d.Contains("doc"));
        
        if (!hasTestDir)
        {
            findings.Add(CreateFinding(
                projectId,
                "Missing Test Directory",
                "No test directory found. Consider adding tests to ensure code quality.",
                Severity.Medium,
                FindingCategory.Structure,
                suggestion: "Create a 'tests' or 'test' directory with unit and integration tests."
            ));
        }
        
        return findings;
    }
}
