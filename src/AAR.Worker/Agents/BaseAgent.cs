using AAR.Application.Interfaces;
using AAR.Domain.Entities;
using AAR.Domain.Enums;
using AAR.Domain.ValueObjects;
using Microsoft.Extensions.Logging;

namespace AAR.Worker.Agents;

/// <summary>
/// Base class for analysis agents providing common functionality.
/// </summary>
public abstract class BaseAgent : IAnalysisAgent
{
    protected readonly IOpenAiService OpenAiService;
    protected readonly ICodeMetricsService MetricsService;
    protected readonly ILogger Logger;

    public abstract AgentType AgentType { get; }

    protected BaseAgent(
        IOpenAiService openAiService, 
        ICodeMetricsService metricsService, 
        ILogger logger)
    {
        OpenAiService = openAiService;
        MetricsService = metricsService;
        Logger = logger;
    }

    public abstract Task<List<ReviewFinding>> AnalyzeAsync(
        Guid projectId, 
        string workingDirectory, 
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets all relevant source files from the working directory.
    /// </summary>
    protected IEnumerable<string> GetSourceFiles(string workingDirectory, params string[] extensions)
    {
        var allExtensions = extensions.Length > 0 
            ? extensions 
            : new[] { ".cs", ".ts", ".tsx", ".js", ".jsx", ".py", ".java", ".go", ".rs", ".cpp", ".c", ".h" };

        return Directory.EnumerateFiles(workingDirectory, "*.*", SearchOption.AllDirectories)
            .Where(f => allExtensions.Any(ext => f.EndsWith(ext, StringComparison.OrdinalIgnoreCase)))
            .Where(f => !IsExcludedPath(f));
    }

    /// <summary>
    /// Checks if a path should be excluded from analysis.
    /// </summary>
    protected bool IsExcludedPath(string path)
    {
        var excludedDirs = new[] 
        { 
            "node_modules", "bin", "obj", ".git", ".vs", ".idea", 
            "packages", "dist", "build", "__pycache__", ".venv", "venv",
            "coverage", ".nyc_output", "TestResults"
        };

        return excludedDirs.Any(dir => 
            path.Contains($"{Path.DirectorySeparatorChar}{dir}{Path.DirectorySeparatorChar}") ||
            path.Contains($"{Path.AltDirectorySeparatorChar}{dir}{Path.AltDirectorySeparatorChar}"));
    }

    /// <summary>
    /// Reads file content with size limits.
    /// </summary>
    protected async Task<string> ReadFileContentAsync(string filePath, int maxLines = 500)
    {
        var lines = await File.ReadAllLinesAsync(filePath);
        
        if (lines.Length <= maxLines)
        {
            return string.Join(Environment.NewLine, lines);
        }

        // Truncate large files
        var truncated = lines.Take(maxLines);
        return string.Join(Environment.NewLine, truncated) + 
            $"{Environment.NewLine}// ... truncated ({lines.Length - maxLines} lines omitted)";
    }

    /// <summary>
    /// Creates a finding with consistent formatting.
    /// </summary>
    protected ReviewFinding CreateFinding(
        Guid projectId,
        string title,
        string description,
        Severity severity,
        FindingCategory category,
        string? filePath = null,
        LineRange? lineRange = null,
        string? codeSnippet = null,
        string? suggestion = null)
    {
        // Use a temporary report ID (will be assigned when report is created)
        var tempReportId = Guid.Empty;
        
        return ReviewFinding.Create(
            projectId: projectId,
            reportId: tempReportId,
            agentType: AgentType,
            category: category,
            severity: severity,
            description: title, // Use title as description
            explanation: description, // Use description as explanation
            filePath: filePath,
            lineRange: lineRange,
            suggestedFix: suggestion,
            originalCodeSnippet: codeSnippet);
    }
}
