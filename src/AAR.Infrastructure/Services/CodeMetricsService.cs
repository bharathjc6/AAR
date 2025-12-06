// =============================================================================
// AAR.Infrastructure - Services/CodeMetricsService.cs
// Service for computing code metrics
// =============================================================================

using AAR.Application.Interfaces;
using AAR.Domain.ValueObjects;
using System.Text.RegularExpressions;

namespace AAR.Infrastructure.Services;

/// <summary>
/// Service for computing code metrics from source files
/// Uses heuristics rather than full parsing for simplicity
/// </summary>
public partial class CodeMetricsService : ICodeMetricsService
{
    /// <inheritdoc/>
    public FileMetrics ComputeMetrics(string content, string filePath)
    {
        var extension = Path.GetExtension(filePath)?.ToLowerInvariant();
        
        if (extension != ".cs")
        {
            // Only compute detailed metrics for C# files
            var lines = content.Split('\n');
            return new FileMetrics
            {
                TotalLines = lines.Length,
                LinesOfCode = lines.Count(l => !string.IsNullOrWhiteSpace(l))
            };
        }

        return ComputeCSharpMetrics(content);
    }

    /// <inheritdoc/>
    public Task<FileMetrics> CalculateMetricsAsync(string content, string filePath, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(ComputeMetrics(content, filePath));
    }

    /// <inheritdoc/>
    public async Task<FileMetrics> CalculateMetricsAsync(string filePath, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var content = await File.ReadAllTextAsync(filePath, cancellationToken);
        return ComputeMetrics(content, filePath);
    }

    /// <inheritdoc/>
    public AggregateMetrics ComputeAggregateMetrics(IEnumerable<FileMetrics> fileMetrics)
    {
        var metricsList = fileMetrics.ToList();
        
        if (metricsList.Count == 0)
        {
            return new AggregateMetrics();
        }

        return new AggregateMetrics
        {
            TotalFiles = metricsList.Count,
            TotalLinesOfCode = metricsList.Sum(m => m.LinesOfCode),
            TotalLines = metricsList.Sum(m => m.TotalLines),
            TotalTypes = metricsList.Sum(m => m.TypeCount),
            TotalMethods = metricsList.Sum(m => m.MethodCount),
            AverageCyclomaticComplexity = metricsList.Count > 0 
                ? metricsList.Average(m => m.CyclomaticComplexity) 
                : 0,
            MaxCyclomaticComplexity = metricsList.Count > 0 
                ? metricsList.Max(m => m.CyclomaticComplexity) 
                : 0
        };
    }

    private static FileMetrics ComputeCSharpMetrics(string content)
    {
        var lines = content.Split('\n');
        var totalLines = lines.Length;
        
        // Count non-blank, non-comment lines
        var linesOfCode = 0;
        var inBlockComment = false;
        
        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            
            // Track block comments
            if (trimmed.StartsWith("/*"))
            {
                inBlockComment = true;
            }
            
            if (inBlockComment)
            {
                if (trimmed.EndsWith("*/"))
                {
                    inBlockComment = false;
                }
                continue;
            }
            
            // Skip empty lines and line comments
            if (string.IsNullOrWhiteSpace(trimmed) || trimmed.StartsWith("//"))
            {
                continue;
            }
            
            linesOfCode++;
        }

        // Count types (class, struct, interface, record, enum)
        var typeCount = TypeRegex().Matches(content).Count;
        
        // Count methods (simplified - looks for access modifier + return type + name + parentheses)
        var methodCount = MethodRegex().Matches(content).Count;
        
        // Count namespaces
        var namespaceCount = NamespaceRegex().Matches(content).Count;
        
        // Estimate cyclomatic complexity (count decision points)
        var cyclomaticComplexity = ComputeCyclomaticComplexity(content);

        return new FileMetrics
        {
            TotalLines = totalLines,
            LinesOfCode = linesOfCode,
            TypeCount = typeCount,
            MethodCount = methodCount,
            NamespaceCount = namespaceCount,
            CyclomaticComplexity = cyclomaticComplexity
        };
    }

    private static int ComputeCyclomaticComplexity(string content)
    {
        // Start with base complexity of 1
        var complexity = 1;
        
        // Count decision points (simplified heuristic)
        var decisionKeywords = new[] { " if ", " if(", " else ", " switch ", " case ", " for ", " for(", 
            " foreach ", " foreach(", " while ", " while(", " catch ", " catch(", " && ", " || ", " ? ", ": " };
        
        foreach (var keyword in decisionKeywords)
        {
            var index = 0;
            while ((index = content.IndexOf(keyword, index, StringComparison.Ordinal)) != -1)
            {
                complexity++;
                index += keyword.Length;
            }
        }

        return complexity;
    }

    [GeneratedRegex(@"\b(class|struct|interface|record|enum)\s+\w+", RegexOptions.Compiled)]
    private static partial Regex TypeRegex();

    [GeneratedRegex(@"\b(public|private|protected|internal)\s+(static\s+)?(async\s+)?[\w<>\[\],\s]+\s+\w+\s*\(", RegexOptions.Compiled)]
    private static partial Regex MethodRegex();

    [GeneratedRegex(@"\bnamespace\s+[\w\.]+", RegexOptions.Compiled)]
    private static partial Regex NamespaceRegex();
}
