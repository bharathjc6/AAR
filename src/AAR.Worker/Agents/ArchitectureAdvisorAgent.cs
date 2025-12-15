using System.Text;
using System.Text.Json;
using AAR.Application.Interfaces;
using AAR.Domain.Entities;
using AAR.Domain.Enums;
using AAR.Domain.ValueObjects;
using Microsoft.Extensions.Logging;

namespace AAR.Worker.Agents;

/// <summary>
/// Analyzes overall architecture, patterns, and provides high-level recommendations.
/// </summary>
public class ArchitectureAdvisorAgent : BaseAgent
{
    public override AgentType AgentType => AgentType.ArchitectureAdvisor;

    public ArchitectureAdvisorAgent(
        IOpenAiService openAiService,
        ICodeMetricsService metricsService,
        ILogger<ArchitectureAdvisorAgent> logger)
        : base(openAiService, metricsService, logger)
    {
    }

    public override async Task<List<ReviewFinding>> AnalyzeAsync(
        Guid projectId,
        string workingDirectory,
        CancellationToken cancellationToken = default)
    {
        Logger.LogInformation("ArchitectureAdvisorAgent analyzing project {ProjectId}", projectId);
        
        var findings = new List<ReviewFinding>();
        
        try
        {
            // Gather project metadata
            var projectInfo = await GatherProjectInfoAsync(workingDirectory, cancellationToken);
            
            // Analyze architecture patterns
            findings.AddRange(await AnalyzeArchitecturePatternsAsync(projectId, projectInfo, cancellationToken));
            
            // Analyze dependencies and coupling
            findings.AddRange(AnalyzeDependencies(projectId, projectInfo));
            
            // Analyze layer violations
            findings.AddRange(AnalyzeLayerViolations(projectId, projectInfo));
            
            // Analyze scalability concerns
            findings.AddRange(AnalyzeScalability(projectId, projectInfo));
            
            // Provide technology recommendations
            findings.AddRange(await ProvideTechnologyRecommendationsAsync(projectId, projectInfo, cancellationToken));
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error in ArchitectureAdvisorAgent analysis");
            findings.Add(CreateFinding(
                projectId,
                "Analysis Error",
                $"Architecture analysis encountered an error: {ex.Message}",
                Severity.Info,
                FindingCategory.Architecture
            ));
        }
        
        Logger.LogInformation("ArchitectureAdvisorAgent found {Count} findings", findings.Count);
        return findings;
    }

    private async Task<ProjectInfo> GatherProjectInfoAsync(string workingDirectory, CancellationToken cancellationToken)
    {
        var info = new ProjectInfo
        {
            RootPath = workingDirectory,
            Directories = new List<string>(),
            FilesByExtension = new Dictionary<string, int>(),
            HasTests = false,
            HasDocker = false,
            HasCiCd = false,
            DetectedFrameworks = new List<string>(),
            DetectedPatterns = new List<string>()
        };
        
        // Gather directory structure
        foreach (var dir in Directory.GetDirectories(workingDirectory, "*", SearchOption.AllDirectories))
        {
            if (IsExcludedPath(dir + Path.DirectorySeparatorChar)) continue;
            info.Directories.Add(Path.GetRelativePath(workingDirectory, dir));
        }
        
        // Count files by extension
        foreach (var file in Directory.GetFiles(workingDirectory, "*.*", SearchOption.AllDirectories))
        {
            if (IsExcludedPath(file)) continue;
            
            var ext = Path.GetExtension(file).ToLowerInvariant();
            if (string.IsNullOrEmpty(ext)) continue;
            
            info.FilesByExtension.TryGetValue(ext, out var count);
            info.FilesByExtension[ext] = count + 1;
        }
        
        // Detect test projects
        info.HasTests = info.Directories.Any(d => 
            d.Contains("test", StringComparison.OrdinalIgnoreCase) ||
            d.Contains("spec", StringComparison.OrdinalIgnoreCase));
        
        // Detect Docker
        info.HasDocker = File.Exists(Path.Combine(workingDirectory, "Dockerfile")) ||
                         File.Exists(Path.Combine(workingDirectory, "docker-compose.yml")) ||
                         File.Exists(Path.Combine(workingDirectory, "docker-compose.yaml"));
        
        // Detect CI/CD
        info.HasCiCd = Directory.Exists(Path.Combine(workingDirectory, ".github", "workflows")) ||
                       Directory.Exists(Path.Combine(workingDirectory, ".azure-pipelines")) ||
                       File.Exists(Path.Combine(workingDirectory, ".gitlab-ci.yml")) ||
                       File.Exists(Path.Combine(workingDirectory, "Jenkinsfile"));
        
        // Detect frameworks
        if (Directory.GetFiles(workingDirectory, "*.csproj", SearchOption.AllDirectories).Any())
        {
            info.DetectedFrameworks.Add(".NET");
            
            var csprojContent = string.Join("\n", 
                await Task.WhenAll(
                    Directory.GetFiles(workingDirectory, "*.csproj", SearchOption.AllDirectories)
                        .Take(5)
                        .Select(f => File.ReadAllTextAsync(f, cancellationToken))));
            
            if (csprojContent.Contains("Microsoft.AspNetCore"))
                info.DetectedFrameworks.Add("ASP.NET Core");
            if (csprojContent.Contains("Microsoft.EntityFrameworkCore"))
                info.DetectedFrameworks.Add("Entity Framework Core");
        }
        
        if (File.Exists(Path.Combine(workingDirectory, "package.json")))
        {
            var packageJson = await File.ReadAllTextAsync(
                Path.Combine(workingDirectory, "package.json"), cancellationToken);
            
            info.DetectedFrameworks.Add("Node.js");
            
            if (packageJson.Contains("\"react\""))
                info.DetectedFrameworks.Add("React");
            if (packageJson.Contains("\"next\""))
                info.DetectedFrameworks.Add("Next.js");
            if (packageJson.Contains("\"angular\""))
                info.DetectedFrameworks.Add("Angular");
            if (packageJson.Contains("\"vue\""))
                info.DetectedFrameworks.Add("Vue");
            if (packageJson.Contains("\"express\""))
                info.DetectedFrameworks.Add("Express");
        }
        
        if (File.Exists(Path.Combine(workingDirectory, "requirements.txt")) ||
            File.Exists(Path.Combine(workingDirectory, "pyproject.toml")))
        {
            info.DetectedFrameworks.Add("Python");
        }
        
        // Detect architectural patterns
        var dirNames = info.Directories.Select(d => d.ToLowerInvariant()).ToHashSet();
        
        if (dirNames.Any(d => d.Contains("domain")) && 
            dirNames.Any(d => d.Contains("application")) &&
            dirNames.Any(d => d.Contains("infrastructure")))
        {
            info.DetectedPatterns.Add("Clean Architecture");
        }
        
        if (dirNames.Any(d => d.Contains("controller")) ||
            dirNames.Any(d => d.Contains("view")) ||
            dirNames.Any(d => d.Contains("model")))
        {
            info.DetectedPatterns.Add("MVC");
        }
        
        if (dirNames.Any(d => d.Contains("services")) ||
            dirNames.Any(d => d.Contains("microservice")))
        {
            info.DetectedPatterns.Add("Service-Oriented");
        }
        
        return info;
    }

    private async Task<List<ReviewFinding>> AnalyzeArchitecturePatternsAsync(
        Guid projectId,
        ProjectInfo info,
        CancellationToken cancellationToken)
    {
        var findings = new List<ReviewFinding>();
        
        // Build a summary for AI analysis
        var summary = new StringBuilder();
        summary.AppendLine("Project Summary:");
        summary.AppendLine($"- Frameworks: {string.Join(", ", info.DetectedFrameworks)}");
        summary.AppendLine($"- Patterns: {string.Join(", ", info.DetectedPatterns)}");
        summary.AppendLine($"- Has Tests: {info.HasTests}");
        summary.AppendLine($"- Has Docker: {info.HasDocker}");
        summary.AppendLine($"- Has CI/CD: {info.HasCiCd}");
        summary.AppendLine($"- File types: {string.Join(", ", info.FilesByExtension.OrderByDescending(x => x.Value).Take(5).Select(x => $"{x.Key}({x.Value})"))}");
        summary.AppendLine();
        summary.AppendLine("Directory Structure:");
        foreach (var dir in info.Directories.Take(50))
        {
            summary.AppendLine($"  {dir}/");
        }
        
        var prompt = $@"As a senior software architect, analyze this project structure and provide architectural insights.

{summary}

Provide findings in these areas:
1. Overall architecture assessment
2. Pattern adherence and anti-patterns
3. Separation of concerns
4. Dependency management approach
5. Scalability considerations

Respond with a JSON array of findings:
[
  {{
    ""title"": ""Finding title"",
    ""description"": ""Detailed explanation"",
    ""severity"": ""Critical|High|Medium|Low|Info"",
    ""category"": ""Pattern|Design|Scalability|BestPractice"",
    ""suggestion"": ""Recommendation""
  }}
]

Only respond with the JSON array.";

        try
        {
            var response = await OpenAiService.AnalyzeCodeAsync(prompt, "ArchitectureAdvisorAgent", cancellationToken);
            
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
                                category: FindingCategory.Architecture,
                                severity: severity,
                                description: f.Title ?? "Architecture Finding",
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
                                f.Title ?? "Architecture Finding",
                                f.Description ?? f.Explanation ?? "",
                                severity,
                                FindingCategory.Architecture,
                                suggestion: f.SuggestedFix
                            ));
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Failed to get AI architecture analysis");
        }
        
        return findings;
    }

    private List<ReviewFinding> AnalyzeDependencies(Guid projectId, ProjectInfo info)
    {
        var findings = new List<ReviewFinding>();
        
        // Check for circular dependency indicators
        var hasSrcAndLib = info.Directories.Any(d => d.StartsWith("src", StringComparison.OrdinalIgnoreCase)) &&
                          info.Directories.Any(d => d.StartsWith("lib", StringComparison.OrdinalIgnoreCase));
        
        if (!hasSrcAndLib && info.Directories.Count > 20)
        {
            findings.Add(CreateFinding(
                projectId,
                "Dependency Organization Review Needed",
                "Large project without clear separation between source and library code.",
                Severity.Low,
                FindingCategory.Architecture,
                suggestion: "Consider organizing code into 'src' for application code and 'lib' for reusable libraries."
            ));
        }
        
        // Check for potential circular dependencies in .NET projects
        if (info.DetectedFrameworks.Contains(".NET"))
        {
            var projectDirs = info.Directories
                .Where(d => !d.Contains(Path.DirectorySeparatorChar) && 
                           !d.StartsWith(".") &&
                           !new[] { "bin", "obj", "packages" }.Contains(d.ToLowerInvariant()))
                .ToList();
            
            if (projectDirs.Count > 5)
            {
                findings.Add(CreateFinding(
                    projectId,
                    "Multiple Projects Detected",
                    $"Found {projectDirs.Count} potential project directories. Verify project references don't create circular dependencies.",
                    Severity.Info,
                    FindingCategory.Architecture,
                    suggestion: "Use a dependency diagram tool to visualize project relationships."
                ));
            }
        }
        
        return findings;
    }

    private List<ReviewFinding> AnalyzeLayerViolations(Guid projectId, ProjectInfo info)
    {
        var findings = new List<ReviewFinding>();
        
        if (info.DetectedPatterns.Contains("Clean Architecture"))
        {
            // Check for potential layer violations
            var domainDir = info.Directories.FirstOrDefault(d => 
                d.EndsWith("Domain", StringComparison.OrdinalIgnoreCase));
            var infraDir = info.Directories.FirstOrDefault(d => 
                d.EndsWith("Infrastructure", StringComparison.OrdinalIgnoreCase));
            
            if (domainDir != null && infraDir != null)
            {
                findings.Add(CreateFinding(
                    projectId,
                    "Clean Architecture Detected",
                    "Project appears to follow Clean Architecture. Ensure Domain layer has no dependencies on Infrastructure.",
                    Severity.Info,
                    FindingCategory.Architecture,
                    suggestion: "Verify that Domain project only references Shared/Core projects, not Infrastructure or Application."
                ));
            }
        }
        
        return findings;
    }

    private List<ReviewFinding> AnalyzeScalability(Guid projectId, ProjectInfo info)
    {
        var findings = new List<ReviewFinding>();
        
        // Check for singleton/static patterns that might affect scalability
        var totalFiles = info.FilesByExtension.Values.Sum();
        
        if (totalFiles > 200 && !info.HasDocker)
        {
            findings.Add(CreateFinding(
                projectId,
                "Missing Containerization",
                "Large project without Docker support. Containerization enables easier deployment and scaling.",
                Severity.Medium,
                FindingCategory.Architecture,
                suggestion: "Add Dockerfile and docker-compose.yml for containerized deployments."
            ));
        }
        
        if (!info.HasCiCd)
        {
            findings.Add(CreateFinding(
                projectId,
                "Missing CI/CD Pipeline",
                "No CI/CD configuration detected. Automated pipelines improve code quality and deployment reliability.",
                Severity.Medium,
                FindingCategory.Architecture,
                suggestion: "Add GitHub Actions, Azure Pipelines, or GitLab CI configuration for automated testing and deployment."
            ));
        }
        
        if (!info.HasTests)
        {
            findings.Add(CreateFinding(
                projectId,
                "Missing Test Project",
                "No test directories found. Tests are essential for maintaining code quality and enabling refactoring.",
                Severity.High,
                FindingCategory.Architecture,
                suggestion: "Add unit tests and integration tests to ensure code reliability."
            ));
        }
        
        return findings;
    }

    private async Task<List<ReviewFinding>> ProvideTechnologyRecommendationsAsync(
        Guid projectId,
        ProjectInfo info,
        CancellationToken cancellationToken)
    {
        var findings = new List<ReviewFinding>();
        
        // Framework-specific recommendations
        if (info.DetectedFrameworks.Contains("ASP.NET Core"))
        {
            // Check for minimal APIs vs controllers
            var hasControllers = info.Directories.Any(d => 
                d.Contains("Controller", StringComparison.OrdinalIgnoreCase));
            
            if (!hasControllers)
            {
                findings.Add(CreateFinding(
                    projectId,
                    "Consider API Organization",
                    "Using ASP.NET Core without traditional controllers. Ensure endpoints are well-organized.",
                    Severity.Info,
                    FindingCategory.BestPractice,
                    suggestion: "For larger APIs, consider grouping endpoints using Carter, FastEndpoints, or minimal API grouping."
                ));
            }
        }
        
        if (info.DetectedFrameworks.Contains("React") || info.DetectedFrameworks.Contains("Vue"))
        {
            var hasStateManagement = info.Directories.Any(d => 
                d.Contains("store", StringComparison.OrdinalIgnoreCase) ||
                d.Contains("redux", StringComparison.OrdinalIgnoreCase) ||
                d.Contains("state", StringComparison.OrdinalIgnoreCase));
            
            if (!hasStateManagement && info.FilesByExtension.GetValueOrDefault(".tsx", 0) + 
                info.FilesByExtension.GetValueOrDefault(".jsx", 0) > 20)
            {
                findings.Add(CreateFinding(
                    projectId,
                    "Consider State Management",
                    "Medium to large React/Vue application without obvious state management directory.",
                    Severity.Low,
                    FindingCategory.BestPractice,
                    suggestion: "For complex state, consider Redux, Zustand, Pinia, or React Query for server state."
                ));
            }
        }
        
        return findings;
    }

    private class ProjectInfo
    {
        public string RootPath { get; set; } = string.Empty;
        public List<string> Directories { get; set; } = new();
        public Dictionary<string, int> FilesByExtension { get; set; } = new();
        public bool HasTests { get; set; }
        public bool HasDocker { get; set; }
        public bool HasCiCd { get; set; }
        public List<string> DetectedFrameworks { get; set; } = new();
        public List<string> DetectedPatterns { get; set; } = new();
    }
}
