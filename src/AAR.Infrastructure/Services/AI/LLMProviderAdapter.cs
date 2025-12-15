// =============================================================================
// AAR.Infrastructure - Services/AI/LLMProviderAdapter.cs
// Adapts ILLMProvider to IOpenAiService for backward compatibility
// =============================================================================

using System.Text.Json;
using AAR.Application.DTOs;
using AAR.Application.Interfaces;
using AAR.Domain.Enums;
using AAR.Shared;
using Microsoft.Extensions.Logging;

namespace AAR.Infrastructure.Services.AI;

/// <summary>
/// Adapts the new ILLMProvider interface to the existing IOpenAiService interface
/// This maintains backward compatibility while using the new provider architecture
/// </summary>
public class LLMProviderAdapter : IOpenAiService
{
    private readonly ILLMProvider _llmProvider;
    private readonly IPromptTemplateProvider _promptProvider;
    private readonly ILogger<LLMProviderAdapter> _logger;

    public bool IsMockMode => false;

    public LLMProviderAdapter(
        ILLMProvider llmProvider,
        IPromptTemplateProvider promptProvider,
        ILogger<LLMProviderAdapter> logger)
    {
        _llmProvider = llmProvider;
        _promptProvider = promptProvider;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task<Result<AgentAnalysisResponse>> AnalyzeAsync(
        AgentType agentType,
        AnalysisContext context,
        IDictionary<string, string> fileContents,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Analyzing with {AgentType} for project {ProjectId}", 
            agentType, context.ProjectId);

        try
        {
            // Get prompts for the agent type
            var systemPrompt = _promptProvider.GetSystemPrompt(agentType);
            var userPrompt = _promptProvider.GetUserPrompt(agentType, context, fileContents);

            // Call LLM
            var request = new LLMRequest
            {
                SystemPrompt = systemPrompt,
                UserPrompt = userPrompt,
                Temperature = 0.3f,
                MaxTokens = 4096
            };

            var result = await _llmProvider.AnalyzeAsync(request, cancellationToken);

            if (!result.IsSuccess)
            {
                _logger.LogError("LLM analysis failed: {Error}", result.Error?.Message);
                return Result<AgentAnalysisResponse>.Failure(result.Error!);
            }

            // Parse JSON response
            var responseText = result.Value!.Content;
            var jsonContent = ExtractJson(responseText);

            if (string.IsNullOrEmpty(jsonContent))
            {
                _logger.LogWarning("No valid JSON found in LLM response for {AgentType}", agentType);
                return Result<AgentAnalysisResponse>.Failure(new Error(
                    "LLM.InvalidResponse",
                    "No valid JSON found in response"));
            }

            var analysisResponse = JsonSerializer.Deserialize<AgentAnalysisResponse>(jsonContent,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            if (analysisResponse is null)
            {
                return Result<AgentAnalysisResponse>.Failure(new Error(
                    "LLM.InvalidResponse",
                    "Failed to deserialize response"));
            }

            _logger.LogInformation("{AgentType} analysis completed with {FindingCount} findings",
                agentType, analysisResponse.Findings.Count);

            return Result<AgentAnalysisResponse>.Success(analysisResponse);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error calling LLM for {AgentType}", agentType);
            return Result<AgentAnalysisResponse>.Failure(new Error(
                "LLM.UnexpectedError",
                $"Unexpected error: {ex.Message}"));
        }
    }

    /// <inheritdoc/>
    public async Task<string> AnalyzeCodeAsync(
        string prompt,
        string agentType,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Analyzing code with agent type: {AgentType}", agentType);

        try
        {
            var request = new LLMRequest
            {
                SystemPrompt = $"You are a {agentType} that analyzes code and returns findings in JSON format.",
                UserPrompt = prompt,
                Temperature = 0.3f,
                MaxTokens = 4096
            };

            var result = await _llmProvider.AnalyzeAsync(request, cancellationToken);

            if (!result.IsSuccess)
            {
                _logger.LogError("LLM analysis failed: {Error}", result.Error?.Message);
                return "[]"; // Return empty JSON array on error
            }

            return result.Value!.Content;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in AnalyzeCodeAsync for {AgentType}", agentType);
            return "[]";
        }
    }

    /// <inheritdoc/>
    public async Task<Result<string>> GenerateSummaryAsync(
        AnalysisContext context,
        IEnumerable<AgentAnalysisResponse> agentResponses,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var responses = agentResponses.ToList();
            var totalFindings = responses.Sum(r => r.Findings.Count);

            var systemPrompt = @"You are an expert software analysis summarizer. 
Generate a comprehensive summary of code analysis findings in JSON format.";

            var userPrompt = $@"Project: {context.ProjectName}

Analysis Results:
{JsonSerializer.Serialize(responses, new JsonSerializerOptions { WriteIndented = true })}

Generate a summary JSON with:
{{
  ""healthScore"": <0-100 score>,
  ""summary"": ""<executive summary>"",
  ""criticalIssues"": <count>,
  ""recommendations"": [""<key recommendations>""]
}}";

            var request = new LLMRequest
            {
                SystemPrompt = systemPrompt,
                UserPrompt = userPrompt,
                Temperature = 0.3f,
                MaxTokens = 2048
            };

            var result = await _llmProvider.AnalyzeAsync(request, cancellationToken);

            if (!result.IsSuccess)
            {
                _logger.LogError("Failed to generate summary: {Error}", result.Error?.Message);
                // Return a basic summary
                return Result<string>.Success(GenerateBasicSummary(context, totalFindings));
            }

            var jsonContent = ExtractJson(result.Value!.Content);
            return Result<string>.Success(jsonContent ?? GenerateBasicSummary(context, totalFindings));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating summary");
            return Result<string>.Failure(new Error(
                "LLM.SummaryError",
                $"Failed to generate summary: {ex.Message}"));
        }
    }

    /// <inheritdoc/>
    public async Task<Result<string>> GeneratePatchAsync(
        string filePath,
        string originalCode,
        string issue,
        string suggestedFix,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var systemPrompt = "You are an expert code fix generator. Generate unified diff patches.";

            var userPrompt = $@"File: {filePath}
Issue: {issue}
Suggested Fix: {suggestedFix}

Original Code:
```
{originalCode}
```

Generate a unified diff patch to fix this issue.";

            var request = new LLMRequest
            {
                SystemPrompt = systemPrompt,
                UserPrompt = userPrompt,
                Temperature = 0.2f,
                MaxTokens = 2048
            };

            var result = await _llmProvider.AnalyzeAsync(request, cancellationToken);

            if (!result.IsSuccess)
            {
                return Result<string>.Failure(result.Error!);
            }

            return Result<string>.Success(result.Value!.Content);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating patch");
            return Result<string>.Failure(new Error(
                "LLM.PatchError",
                $"Failed to generate patch: {ex.Message}"));
        }
    }

    /// <inheritdoc/>
    public async Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default)
    {
        return await _llmProvider.IsAvailableAsync(cancellationToken);
    }

    /// <summary>
    /// Extracts JSON content from LLM response (handles markdown code blocks)
    /// </summary>
    private static string? ExtractJson(string response)
    {
        if (string.IsNullOrWhiteSpace(response))
            return null;

        // Try to find JSON in markdown code blocks
        var jsonBlockStart = response.IndexOf("```json", StringComparison.OrdinalIgnoreCase);
        if (jsonBlockStart >= 0)
        {
            var contentStart = response.IndexOf('\n', jsonBlockStart) + 1;
            var contentEnd = response.IndexOf("```", contentStart);
            if (contentEnd > contentStart)
            {
                return response.Substring(contentStart, contentEnd - contentStart).Trim();
            }
        }

        // Try to find JSON without markdown
        var jsonStart = response.IndexOf('{');
        var jsonArrayStart = response.IndexOf('[');

        if (jsonStart < 0 && jsonArrayStart < 0)
            return null;

        int start = jsonStart >= 0 && jsonArrayStart >= 0
            ? Math.Min(jsonStart, jsonArrayStart)
            : Math.Max(jsonStart, jsonArrayStart);

        // Find the matching closing bracket
        int end = -1;
        int depth = 0;
        char startChar = response[start];
        char endChar = startChar == '{' ? '}' : ']';

        for (int i = start; i < response.Length; i++)
        {
            if (response[i] == startChar) depth++;
            if (response[i] == endChar) depth--;
            if (depth == 0)
            {
                end = i;
                break;
            }
        }

        if (end > start)
        {
            return response.Substring(start, end - start + 1);
        }

        return null;
    }

    private static string GenerateBasicSummary(AnalysisContext context, int totalFindings)
    {
        return JsonSerializer.Serialize(new
        {
            healthScore = totalFindings == 0 ? 100 : Math.Max(0, 100 - (totalFindings * 5)),
            summary = $"Analysis completed for {context.ProjectName}. Found {totalFindings} issues.",
            criticalIssues = 0,
            recommendations = new[] { "Review the findings and address high-severity issues first." }
        });
    }
}
