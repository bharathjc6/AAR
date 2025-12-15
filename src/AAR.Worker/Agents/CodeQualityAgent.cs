using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using AAR.Application.Configuration;
using AAR.Application.Interfaces;
using AAR.Domain.Entities;
using AAR.Domain.Enums;
using AAR.Domain.ValueObjects;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Polly.Timeout;

namespace AAR.Worker.Agents;

/// <summary>
/// Analyzes code quality using cluster-based approach.
/// PHASE 1: Static analysis (no LLM) - extract metrics for all files
/// PHASE 2: Cluster files by similarity
/// PHASE 3: Batch LLM analysis on clusters (not per-file)
/// PHASE 4: Optional per-file deep dive for high-complexity files
/// </summary>
public class CodeQualityAgent : BaseAgent
{
    private readonly IStaticAnalyzer _staticAnalyzer;
    private readonly IClusterBuilder _clusterBuilder;
    private readonly ClusterAnalysisOptions _clusterOptions;

    public override AgentType AgentType => AgentType.CodeQuality;

    public CodeQualityAgent(
        IOpenAiService openAiService,
        ICodeMetricsService metricsService,
        IStaticAnalyzer staticAnalyzer,
        IClusterBuilder clusterBuilder,
        IOptions<ClusterAnalysisOptions> clusterOptions,
        ILogger<CodeQualityAgent> logger)
        : base(openAiService, metricsService, logger)
    {
        _staticAnalyzer = staticAnalyzer;
        _clusterBuilder = clusterBuilder;
        _clusterOptions = clusterOptions.Value;
    }

    public override async Task<List<ReviewFinding>> AnalyzeAsync(
        Guid projectId,
        string workingDirectory,
        CancellationToken cancellationToken = default)
    {
        Logger.LogInformation("CodeQualityAgent starting cluster-based analysis for project {ProjectId}", projectId);
        
        var findings = new List<ReviewFinding>();

        try
        {
            // ===== PHASE 1: FAST STATIC ANALYSIS (NO LLM) =====
            Logger.LogInformation("CodeQualityAgent: PHASE 1 - Static analysis");
            var fileSummaries = await _staticAnalyzer.AnalyzeProjectAsync(workingDirectory, cancellationToken);
            Logger.LogInformation("CodeQualityAgent: Phase 1 complete - analyzed {Count} files", fileSummaries.Count);

            if (fileSummaries.Count == 0)
            {
                Logger.LogWarning("CodeQualityAgent: No source files found in {Directory}", workingDirectory);
                return findings;
            }

            // Add rule-based findings from static analysis
            findings.AddRange(GenerateStaticFindingsFromMetrics(projectId, fileSummaries));

            // Check if cluster analysis is enabled
            if (!_clusterOptions.EnableClusterAnalysis)
            {
                Logger.LogInformation("CodeQualityAgent: Cluster analysis disabled, falling back to legacy per-file mode");
                findings.AddRange(await LegacyPerFileAnalysisAsync(projectId, workingDirectory, fileSummaries, cancellationToken));
                return findings;
            }

            // ===== PHASE 2: CLUSTERING =====
            Logger.LogInformation("CodeQualityAgent: PHASE 2 - Building clusters");
            var clusters = await _clusterBuilder.BuildClustersAsync(fileSummaries, cancellationToken);
            Logger.LogInformation("CodeQualityAgent: Phase 2 complete - created {Count} clusters", clusters.Count);

            // ===== PHASE 3: BATCHED LLM ANALYSIS ON CLUSTERS =====
            Logger.LogInformation("CodeQualityAgent: PHASE 3 - Batch LLM analysis on clusters");
            findings.AddRange(await AnalyzeClustersWithLLMAsync(projectId, clusters, workingDirectory, cancellationToken));
            Logger.LogInformation("CodeQualityAgent: Phase 3 complete - LLM analysis of clusters finished");

            // ===== PHASE 4: OPTIONAL TARGETED DEEP DIVE =====
            if (!_clusterOptions.AlwaysDeepDiveAllFiles)
            {
                Logger.LogInformation("CodeQualityAgent: PHASE 4 - Detecting high-priority files for deep dive");
                var deepDiveFiles = _clusterBuilder.DetectHighPriorityFiles(
                    fileSummaries,
                    _clusterOptions.DeepDiveComplexityThreshold,
                    _clusterOptions.DeepDiveLineCountThreshold);

                if (deepDiveFiles.Count > 0)
                {
                    Logger.LogInformation("CodeQualityAgent: Phase 4 - Analyzing {Count} high-priority files", deepDiveFiles.Count);
                    findings.AddRange(await DeepDiveAnalysisAsync(projectId, deepDiveFiles, workingDirectory, cancellationToken));
                }
            }
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error in CodeQualityAgent cluster analysis");
            findings.Add(CreateFinding(
                projectId,
                "Analysis Error",
                $"Code quality analysis encountered an error: {ex.Message}",
                Severity.Info,
                FindingCategory.CodeQuality
            ));
        }
        
        Logger.LogInformation("CodeQualityAgent found {Count} total findings", findings.Count);
        return findings;
    }

    /// <summary>
    /// PHASE 1: Generate findings from static metrics (no LLM)
    /// </summary>
    private List<ReviewFinding> GenerateStaticFindingsFromMetrics(Guid projectId, List<FileSummary> fileSummaries)
    {
        var findings = new List<ReviewFinding>();

        foreach (var file in fileSummaries)
        {
            // High complexity
            if (file.MaxCyclomaticComplexity > 20)
            {
                findings.Add(CreateFinding(
                    projectId,
                    "High Cyclomatic Complexity",
                    $"File has cyclomatic complexity of {file.MaxCyclomaticComplexity:F1}. Complex code is harder to test and maintain.",
                    file.MaxCyclomaticComplexity > 30 ? Severity.High : Severity.Medium,
                    FindingCategory.Complexity,
                    filePath: file.RelativePath,
                    suggestion: "Consider breaking down complex methods into smaller, focused functions."
                ));
            }

            // Long files
            if (file.LinesOfCode > 500)
            {
                findings.Add(CreateFinding(
                    projectId,
                    "Long File",
                    $"File has {file.LinesOfCode} lines of code. Long files are harder to navigate and maintain.",
                    file.LinesOfCode > 1000 ? Severity.Medium : Severity.Low,
                    FindingCategory.Maintainability,
                    filePath: file.RelativePath,
                    suggestion: "Consider splitting this file into multiple smaller, focused files."
                ));
            }

            // Too many methods
            if (file.MethodCount > 20)
            {
                findings.Add(CreateFinding(
                    projectId,
                    "Too Many Methods",
                    $"File contains {file.MethodCount} methods/functions. This may indicate the class is doing too much.",
                    Severity.Medium,
                    FindingCategory.Maintainability,
                    filePath: file.RelativePath,
                    suggestion: "Consider applying the Single Responsibility Principle and splitting into multiple classes."
                ));
            }
        }

        return findings;
    }

    /// <summary>
    /// PHASE 3: Analyze clusters with LLM (batch analysis, not per-file)
    /// </summary>
    private async Task<List<ReviewFinding>> AnalyzeClustersWithLLMAsync(
        Guid projectId,
        List<AnalysisCluster> clusters,
        string workingDirectory,
        CancellationToken cancellationToken = default)
    {
        var findings = new List<ReviewFinding>();
        var semaphore = new SemaphoreSlim(_clusterOptions.MaxParallelLLMCalls);

        var tasks = clusters
            .Where(c => !c.IsAnalyzed)
            .Select(async cluster =>
            {
                await semaphore.WaitAsync(cancellationToken);
                try
                {
                    return await AnalyzeSingleClusterWithLLMAsync(projectId, cluster, workingDirectory, cancellationToken);
                }
                finally
                {
                    semaphore.Release();
                }
            });

        var batchResults = await Task.WhenAll(tasks);
        findings.AddRange(batchResults.SelectMany(x => x));

        return findings;
    }

    /// <summary>
    /// Analyze a single cluster with a batch LLM call
    /// </summary>
    private async Task<List<ReviewFinding>> AnalyzeSingleClusterWithLLMAsync(
        Guid projectId,
        AnalysisCluster cluster,
        string workingDirectory,
        CancellationToken cancellationToken)
    {
        var findings = new List<ReviewFinding>();

        Logger.LogInformation("CodeQualityAgent: Analyzing cluster '{Name}' with {Count} files",
            cluster.Name, cluster.Files.Count);

        try
        {
            // Build a summary of the cluster for the LLM
            var clusterSummary = BuildClusterSummary(cluster);
            var prompt = BuildClusterAnalysisPrompt(cluster, clusterSummary);

            try
            {
                var response = await OpenAiService.AnalyzeCodeAsync(
                    prompt,
                    $"CodeQualityAgent-Cluster-{cluster.Name}",
                    cancellationToken);

                var clusterFindings = ParseClusterAnalysisResponse(projectId, cluster, response);
                findings.AddRange(clusterFindings);

                cluster.IsAnalyzed = true;
            }
            catch (Polly.Timeout.TimeoutRejectedException ex)
            {
                // Graceful degradation: LLM timeout - generate static findings instead
                Logger.LogWarning(ex, 
                    "LLM timed out analyzing cluster '{Name}' ({Files} files). " +
                    "Falling back to static metrics-based findings.",
                    cluster.Name, cluster.Files.Count);

                // Generate metrics-based findings without LLM
                var metricFindings = GenerateStaticFindingsFromMetrics(projectId, cluster.Files);
                findings.AddRange(metricFindings);

                cluster.IsAnalyzed = true;
            }
            catch (TaskCanceledException ex) when (ex.CancellationToken == cancellationToken)
            {
                // Operation was explicitly cancelled, not a timeout
                Logger.LogWarning("Cluster analysis cancelled for '{Name}'", cluster.Name);
                throw;
            }
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Failed to analyze cluster '{Name}'", cluster.Name);
        }

        return findings;
    }

    /// <summary>
    /// PHASE 4: Targeted deep dive analysis for high-priority files
    /// Includes per-file timeout and graceful degradation for large files
    /// </summary>
    private async Task<List<ReviewFinding>> DeepDiveAnalysisAsync(
        Guid projectId,
        List<FileSummary> files,
        string workingDirectory,
        CancellationToken cancellationToken)
    {
        var findings = new List<ReviewFinding>();
        var semaphore = new SemaphoreSlim(_clusterOptions.MaxParallelLLMCalls);

        // Limit deep dive to top N files to prevent excessive LLM calls
        var filesToAnalyze = files
            .OrderByDescending(f => f.MaxCyclomaticComplexity)
            .ThenByDescending(f => f.LinesOfCode)
            .Take(5) // Limit to top 5 high-priority files
            .ToList();

        Logger.LogInformation("Deep dive analysis for {Count} high-priority files (limited from {Total})",
            filesToAnalyze.Count, files.Count);

        foreach (var file in filesToAnalyze)
        {
            if (cancellationToken.IsCancellationRequested) break;

            await semaphore.WaitAsync(cancellationToken);
            try
            {
                var filePath = Path.Combine(workingDirectory, file.RelativePath);
                var content = await ReadFileContentAsync(filePath);
                
                try
                {
                    var fileFindings = await AnalyzeFileWithAiAsync(projectId, file.RelativePath, content, cancellationToken);
                    findings.AddRange(fileFindings);
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    // Per-file timeout (not job cancellation)
                    Logger.LogWarning("Deep dive timed out for file {File}. Generating static findings instead.", 
                        file.RelativePath);
                    
                    // Fall back to static finding for this file
                    findings.Add(CreateFinding(
                        projectId,
                        "High Complexity File Requires Manual Review",
                        $"File {file.RelativePath} has high complexity (max cyclomatic: {file.MaxCyclomaticComplexity:F0}, {file.LinesOfCode} LOC) but automated analysis timed out. Manual code review recommended.",
                        Severity.Medium,
                        FindingCategory.CodeQuality,
                        filePath: file.RelativePath,
                        lineRange: null,
                        codeSnippet: null,
                        suggestion: "Consider breaking down complex methods and reducing file size."
                    ));
                }
            }
            catch (Exception ex)
            {
                Logger.LogWarning(ex, "Failed to deep dive analyze file {File}", file.RelativePath);
            }
            finally
            {
                semaphore.Release();
            }
        }

        return findings;
    }

    /// <summary>
    /// Legacy per-file analysis fallback (when cluster analysis is disabled)
    /// </summary>
    private async Task<List<ReviewFinding>> LegacyPerFileAnalysisAsync(
        Guid projectId,
        string workingDirectory,
        List<FileSummary> fileSummaries,
        CancellationToken cancellationToken)
    {
        var findings = new List<ReviewFinding>();
        var filesToAnalyze = fileSummaries
            .OrderByDescending(f => f.LinesOfCode)
            .Take(10)
            .ToList();

        foreach (var file in filesToAnalyze)
        {
            if (cancellationToken.IsCancellationRequested) break;

            try
            {
                var filePath = Path.Combine(workingDirectory, file.RelativePath);
                var content = await ReadFileContentAsync(filePath);
                var fileFindings = await AnalyzeFileWithAiAsync(projectId, file.RelativePath, content, cancellationToken);
                findings.AddRange(fileFindings);
            }
            catch (Exception ex)
            {
                Logger.LogWarning(ex, "Failed to analyze file {File}", file.RelativePath);
            }
        }

        return findings;
    }

    /// <summary>
    /// Build a text summary of the cluster for the LLM prompt (concise version for faster LLM processing)
    /// </summary>
    private string BuildClusterSummary(AnalysisCluster cluster)
    {
        var topFiles = cluster.Files
            .OrderByDescending(f => f.MaxCyclomaticComplexity)
            .ThenByDescending(f => f.LinesOfCode)
            .Take(5) // Only top 5 files to keep prompt small
            .ToList();

        var sb = new StringBuilder();
        sb.AppendLine($"{cluster.Name} | {cluster.Theme} | {cluster.Files.Count} files | {cluster.TotalLinesOfCode} LOC | Risk: {cluster.RiskLevel}");
        sb.AppendLine();
        sb.AppendLine("Key files:");
        
        foreach (var file in topFiles)
        {
            sb.AppendLine($"  {Path.GetFileName(file.RelativePath)} ({file.LinesOfCode}L, complexity:{file.MaxCyclomaticComplexity:F0})");
        }

        if (cluster.Files.Count > 5)
        {
            sb.AppendLine($"  +{cluster.Files.Count - 5} more");
        }

        return sb.ToString();
    }

    /// <summary>
    /// Build the LLM prompt for cluster analysis
    /// </summary>
    private string BuildClusterAnalysisPrompt(AnalysisCluster cluster, string clusterSummary)
    {
        return $@"Analyze this code cluster ({cluster.Files.Count} files) for patterns and issues.

CLUSTER: {cluster.Name} ({cluster.Theme})
{clusterSummary}

Focus on:
- Common patterns/duplication
- Architectural issues
- Security risks
- Performance concerns

Output JSON array (empty if no issues):
[{{""id"":""uid"",""description"":""issue"",""severity"":""Critical|High|Medium|Low"",""category"":""CodeQuality"",""affectedFiles"":[],""suggestedFix"":""fix""}}]";
    }

    /// <summary>
    /// Parse LLM response for cluster analysis
    /// </summary>
    private List<ReviewFinding> ParseClusterAnalysisResponse(
        Guid projectId,
        AnalysisCluster cluster,
        string response)
    {
        var findings = new List<ReviewFinding>();

        try
        {
            var jsonStart = response.IndexOf('[');
            var jsonEnd = response.LastIndexOf(']');

            if (jsonStart < 0 || jsonEnd <= jsonStart) return findings;

            var json = response.Substring(jsonStart, jsonEnd - jsonStart + 1);
            var parsed = JsonSerializer.Deserialize<List<ClusterAiFinding>>(json, AiFindingModels.JsonOptions);

            if (parsed == null) return findings;

            foreach (var f in parsed)
            {
                var severity = Enum.TryParse<Severity>(f.Severity, ignoreCase: true, out var s)
                    ? s : Severity.Info;

                // For cluster findings, reference the cluster name
                var description = $"[Cluster: {cluster.Name}] {f.Description}";

                findings.Add(CreateFinding(
                    projectId,
                    f.Description ?? "Code Quality Issue",
                    f.Explanation ?? "",
                    severity,
                    FindingCategory.CodeQuality,
                    filePath: cluster.Name,  // Use cluster name as reference
                    suggestion: f.SuggestedFix
                ));
            }
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Failed to parse cluster analysis response for cluster '{Name}'", cluster.Name);
        }

        return findings;
    }

    private async Task<List<ReviewFinding>> AnalyzeFileWithAiAsync(
        Guid projectId,
        string relativePath,
        string content,
        CancellationToken cancellationToken)
    {
        var findings = new List<ReviewFinding>();
        
        // Limit content size to prevent LLM timeout (max ~8000 chars = ~2000 tokens)
        const int MaxContentLength = 8000;
        var truncated = false;
        
        if (content.Length > MaxContentLength)
        {
            Logger.LogWarning(
                "File {File} is too large ({Length} chars). Truncating to {Max} chars for LLM analysis.",
                relativePath, content.Length, MaxContentLength);
            content = content.Substring(0, MaxContentLength) + "\n\n// ... [TRUNCATED - file too large for full analysis]";
            truncated = true;
        }
        
        // Normalize path for JSON - use forward slashes to avoid JSON parsing errors with unescaped backslashes
        var normalizedPath = relativePath.Replace("\\", "/");
        
        var prompt = $@"Analyze the following code file for quality issues and provide only evidence-backed findings.
Each finding MUST include either a `filePath` or a `symbol` and a `confidence` score between 0.0 and 1.0. Do not emit findings without verifiable evidence.
{(truncated ? "NOTE: This file was truncated. Focus on patterns visible in the shown portion." : "")}

File: {normalizedPath}

```
{content}
```

Respond with a JSON array of findings using this schema:
[
    {{
        ""id"": ""unique-id"",
        ""description"": ""issue description"",
        ""severity"": ""Critical|High|Medium|Low"",
        ""lineRange"": {{ ""start"": 45, ""end"": 60 }},
        ""confidence"": 0.92,
        ""suggestedFix"": ""how to fix""
    }}
]

Only output the JSON array. If no issues, output [].";

        try
        {
            // Use a per-file timeout to prevent single files from blocking entire analysis
            using var fileTimeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            fileTimeoutCts.CancelAfter(TimeSpan.FromMinutes(3)); // 3-minute max per file
            
            var response = await OpenAiService.AnalyzeCodeAsync(prompt, "CodeQualityAgent", fileTimeoutCts.Token);
            
            var jsonStart = response.IndexOf('[');
            var jsonEnd = response.LastIndexOf(']');
            
            if (jsonStart >= 0 && jsonEnd > jsonStart)
            {
                var json = response.Substring(jsonStart, jsonEnd - jsonStart + 1);
                
                try
                {
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

                            // If AI provided symbol or confidence, create domain ReviewFinding with those values
                            if (!string.IsNullOrWhiteSpace(f.Symbol) || (f.Confidence > 0))
                            {
                                findings.Add(ReviewFinding.Create(
                                    projectId: projectId,
                                    reportId: Guid.Empty,
                                    agentType: AgentType,
                                    category: FindingCategory.CodeQuality,
                                    severity: severity,
                                    description: f.Title ?? "Code Quality Issue",
                                    explanation: f.Explanation ?? f.Description ?? "",
                                    filePath: relativePath,
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
                                    f.Title ?? "Code Quality Issue",
                                    f.Description ?? f.Explanation ?? "",
                                    severity,
                                    FindingCategory.CodeQuality,
                                    filePath: relativePath,
                                    lineRange: lineRange,
                                    codeSnippet: f.CodeSnippet,
                                    suggestion: f.SuggestedFix
                                ));
                            }
                        }
                    }
                }
                catch (System.Text.Json.JsonException jsonEx)
                {
                    // Log JSON parsing error with context - likely unescaped backslashes in file paths
                    Logger.LogWarning(jsonEx, 
                        "Failed to parse JSON response for file {File}. " +
                        "Likely cause: unescaped backslashes in file paths. Response preview: {Preview}",
                        relativePath, json.Substring(0, Math.Min(200, json.Length)));
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
}
