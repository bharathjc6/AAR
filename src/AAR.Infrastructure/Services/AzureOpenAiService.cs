// =============================================================================
// AAR.Infrastructure - Services/AzureOpenAiService.cs
// Azure OpenAI implementation with mock mode fallback
// =============================================================================

using AAR.Application.DTOs;
using AAR.Application.Interfaces;
using AAR.Domain.Enums;
using AAR.Shared;
using Azure;
using Azure.AI.OpenAI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenAI.Chat;
using System.ClientModel;
using System.Text;
using System.Text.Json;

namespace AAR.Infrastructure.Services;

/// <summary>
/// Azure OpenAI service implementation
/// Falls back to mock mode when credentials are not configured
/// </summary>
public class AzureOpenAiService : IOpenAiService
{
    private readonly AzureOpenAiOptions _options;
    private readonly ILogger<AzureOpenAiService> _logger;
    private readonly AzureOpenAIClient? _client;
    private readonly IPromptTemplateProvider _promptProvider;

    /// <inheritdoc/>
    public bool IsMockMode { get; }

    public AzureOpenAiService(
        IOptions<AzureOpenAiOptions> options,
        IPromptTemplateProvider promptProvider,
        ILogger<AzureOpenAiService> logger)
    {
        _options = options.Value;
        _promptProvider = promptProvider;
        _logger = logger;

        // Get endpoint from options or environment variable
        var endpoint = _options.Endpoint ?? Environment.GetEnvironmentVariable("AZURE_OPENAI_ENDPOINT");
        var apiKey = _options.ApiKey ?? Environment.GetEnvironmentVariable("AZURE_OPENAI_KEY");

        // Validate credentials: must be non-empty, not contain TODO placeholders, and endpoint must be valid URI
        var hasValidEndpoint = !string.IsNullOrWhiteSpace(endpoint) 
            && !endpoint.Contains("TODO", StringComparison.OrdinalIgnoreCase)
            && Uri.TryCreate(endpoint, UriKind.Absolute, out var endpointUri)
            && (endpointUri.Scheme == Uri.UriSchemeHttps || endpointUri.Scheme == Uri.UriSchemeHttp);
        
        var hasValidApiKey = !string.IsNullOrWhiteSpace(apiKey) 
            && !apiKey.Contains("TODO", StringComparison.OrdinalIgnoreCase);

        if (hasValidEndpoint && hasValidApiKey)
        {
            try
            {
                _client = new AzureOpenAIClient(new Uri(endpoint!), new AzureKeyCredential(apiKey!));
                IsMockMode = false;
                _logger.LogInformation("AzureOpenAiService initialized with real Azure OpenAI client at {Endpoint}", endpoint);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to initialize Azure OpenAI client. Using mock mode.");
                IsMockMode = true;
            }
        }
        else
        {
            _logger.LogInformation("Azure OpenAI credentials not configured or invalid. Using mock mode. Endpoint present: {HasEndpoint}, ApiKey present: {HasApiKey}", 
                !string.IsNullOrWhiteSpace(endpoint), !string.IsNullOrWhiteSpace(apiKey));
            IsMockMode = true;
        }
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

        if (IsMockMode)
        {
            return await GenerateMockResponseAsync(agentType, context, fileContents, cancellationToken);
        }

        try
        {
            // Get prompt template for this agent type
            var systemPrompt = _promptProvider.GetSystemPrompt(agentType);
            var userPrompt = _promptProvider.GetUserPrompt(agentType, context, fileContents);

            // Call Azure OpenAI
            var chatClient = _client!.GetChatClient(_options.DeploymentName ?? "gpt-4");
            
            var messages = new List<ChatMessage>
            {
                new SystemChatMessage(systemPrompt),
                new UserChatMessage(userPrompt)
            };

            var options = new ChatCompletionOptions
            {
                MaxOutputTokenCount = 4096,
                Temperature = 0.3f
            };

            var response = await chatClient.CompleteChatAsync(messages, options, cancellationToken);

            // Parse response
            var responseText = response.Value.Content[0].Text;
            
            // Try to extract JSON from the response
            var jsonContent = ExtractJson(responseText);
            
            if (string.IsNullOrEmpty(jsonContent))
            {
                _logger.LogWarning("No valid JSON found in OpenAI response for {AgentType}", agentType);
                return DomainErrors.OpenAI.InvalidResponse;
            }

            var analysisResponse = JsonSerializer.Deserialize<AgentAnalysisResponse>(jsonContent, 
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            
            if (analysisResponse is null)
            {
                return DomainErrors.OpenAI.InvalidResponse;
            }

            _logger.LogInformation("{AgentType} analysis completed with {FindingCount} findings",
                agentType, analysisResponse.Findings.Count);

            return analysisResponse;
        }
        catch (ClientResultException ex) when (ex.Status == 429)
        {
            _logger.LogWarning("Rate limited by Azure OpenAI");
            return DomainErrors.OpenAI.RateLimited;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error calling Azure OpenAI for {AgentType}", agentType);
            return DomainErrors.OpenAI.ServiceUnavailable;
        }
    }

    /// <inheritdoc/>
    public async Task<string> AnalyzeCodeAsync(string prompt, string agentType, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        
        _logger.LogInformation("Analyzing code with agent type: {AgentType}", agentType);
        
        if (IsMockMode)
        {
            // Return mock JSON response based on agent type
            await Task.Delay(100, cancellationToken); // Simulate processing
            return GenerateMockJsonResponse(agentType);
        }
        
        try
        {
            var chatClient = _client!.GetChatClient(_options.DeploymentName ?? "gpt-4");
            
            var messages = new List<ChatMessage>
            {
                new SystemChatMessage($"You are a {agentType} that analyzes code and returns findings in JSON format."),
                new UserChatMessage(prompt)
            };
            
            var options = new ChatCompletionOptions
            {
                MaxOutputTokenCount = 4096,
                Temperature = 0.3f
            };
            
            var response = await chatClient.CompleteChatAsync(messages, options, cancellationToken);
            return response.Value.Content[0].Text;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in AnalyzeCodeAsync for {AgentType}", agentType);
            return "[]"; // Return empty JSON array on error
        }
    }

    private static string GenerateMockJsonResponse(string agentType)
    {
        return agentType switch
        {
            "StructureAgent" => """[{"title":"Project Structure","description":"Mock structure finding","severity":"Info"}]""",
            "SecurityAgent" => """[{"title":"Security Check","description":"Mock security finding","severity":"Medium"}]""",
            "CodeQualityAgent" => """[{"title":"Code Quality","description":"Mock quality finding","severity":"Low"}]""",
            "ArchitectureAdvisorAgent" => """[{"title":"Architecture Review","description":"Mock architecture finding","severity":"Info"}]""",
            _ => "[]"
        };
    }

    /// <inheritdoc/>
    public async Task<Result<string>> GenerateSummaryAsync(
        AnalysisContext context,
        IEnumerable<AgentAnalysisResponse> agentResponses,
        CancellationToken cancellationToken = default)
    {
        if (IsMockMode)
        {
            return GenerateMockSummary(context, agentResponses);
        }

        // Implementation for real OpenAI call
        var sb = new StringBuilder();
        foreach (var response in agentResponses)
        {
            sb.AppendLine(response.Summary);
        }
        
        return await Task.FromResult(sb.ToString());
    }

    /// <inheritdoc/>
    public async Task<Result<string>> GeneratePatchAsync(
        string filePath,
        string originalCode,
        string issue,
        string suggestedFix,
        CancellationToken cancellationToken = default)
    {
        if (IsMockMode)
        {
            // Mock patch generation
            return await Task.FromResult($"--- a/{filePath}\n+++ b/{filePath}\n@@ Mock patch for: {issue} @@");
        }

        // TODO: Implement real patch generation with OpenAI
        return await Task.FromResult($"--- a/{filePath}\n+++ b/{filePath}\n@@ Suggested fix: {suggestedFix} @@");
    }

    /// <inheritdoc/>
    public async Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default)
    {
        if (IsMockMode)
        {
            return await Task.FromResult(true); // Mock is always available
        }

        try
        {
            // Simple availability check
            var chatClient = _client!.GetChatClient(_options.DeploymentName ?? "gpt-4");
            return chatClient is not null;
        }
        catch
        {
            return false;
        }
    }

    private async Task<Result<AgentAnalysisResponse>> GenerateMockResponseAsync(
        AgentType agentType,
        AnalysisContext context,
        IDictionary<string, string> fileContents,
        CancellationToken cancellationToken)
    {
        // Simulate processing time
        await Task.Delay(500, cancellationToken);

        var findings = new List<AgentFinding>();
        var fileList = fileContents.Keys.ToList();

        // Generate mock findings based on agent type
        switch (agentType)
        {
            case AgentType.Structure:
                findings.AddRange(GenerateMockStructureFindings(fileList));
                break;
            case AgentType.CodeQuality:
                findings.AddRange(GenerateMockCodeQualityFindings(fileContents));
                break;
            case AgentType.Security:
                findings.AddRange(GenerateMockSecurityFindings(fileContents));
                break;
            case AgentType.ArchitectureAdvisor:
                findings.AddRange(GenerateMockArchitectureFindings(fileList));
                break;
        }

        var summary = $"[MOCK] {agentType} analysis completed. Found {findings.Count} issues.";
        var recommendations = GenerateMockRecommendations(agentType);

        return new AgentAnalysisResponse
        {
            Findings = findings,
            Summary = summary,
            Recommendations = recommendations
        };
    }

    private static List<AgentFinding> GenerateMockStructureFindings(List<string> files)
    {
        var findings = new List<AgentFinding>();

        // Check for common structural issues
        if (!files.Any(f => f.Contains("README", StringComparison.OrdinalIgnoreCase)))
        {
            findings.Add(new AgentFinding
            {
                Id = Guid.NewGuid().ToString(),
                Category = "Structure",
                Severity = "Low",
                Description = "Missing README.md file",
                Explanation = "A README.md file helps developers understand the project structure and setup instructions.",
                SuggestedFix = "Add a README.md file with project documentation."
            });
        }

        if (files.Count > 50)
        {
            findings.Add(new AgentFinding
            {
                Id = Guid.NewGuid().ToString(),
                Category = "Structure",
                Severity = "Medium",
                Description = "Large number of files without clear organization",
                Explanation = "Projects with many files should be organized into logical folders.",
                SuggestedFix = "Consider organizing files into feature-based or layer-based folders."
            });
        }

        return findings;
    }

    private static List<AgentFinding> GenerateMockCodeQualityFindings(IDictionary<string, string> fileContents)
    {
        var findings = new List<AgentFinding>();

        foreach (var (path, content) in fileContents.Take(5))
        {
            var lines = content.Split('\n');
            
            // Check for long methods (simplified check)
            if (lines.Length > 100)
            {
                findings.Add(new AgentFinding
                {
                    Id = Guid.NewGuid().ToString(),
                    FilePath = path,
                    Category = "CodeQuality",
                    Severity = "Medium",
                    Description = "File has many lines - consider breaking into smaller units",
                    Explanation = "Long files can be hard to maintain and understand.",
                    SuggestedFix = "Extract related functionality into separate classes or methods."
                });
            }

            // Check for TODO comments
            for (int i = 0; i < lines.Length; i++)
            {
                if (lines[i].Contains("TODO", StringComparison.OrdinalIgnoreCase))
                {
                    findings.Add(new AgentFinding
                    {
                        Id = Guid.NewGuid().ToString(),
                        FilePath = path,
                        LineRange = new AgentLineRange { Start = i + 1, End = i + 1 },
                        Category = "CodeQuality",
                        Severity = "Low",
                        Description = "TODO comment found",
                        Explanation = "TODO comments indicate incomplete work that should be addressed.",
                        OriginalCodeSnippet = lines[i].Trim()
                    });
                    break; // Only report first TODO per file
                }
            }
        }

        return findings;
    }

    private static List<AgentFinding> GenerateMockSecurityFindings(IDictionary<string, string> fileContents)
    {
        var findings = new List<AgentFinding>();
        var sensitivePatterns = new[] { "password", "secret", "apikey", "connectionstring" };

        foreach (var (path, content) in fileContents)
        {
            var lines = content.Split('\n');
            
            for (int i = 0; i < lines.Length; i++)
            {
                var line = lines[i].ToLowerInvariant();
                
                foreach (var pattern in sensitivePatterns)
                {
                    if (line.Contains(pattern) && (line.Contains("=") || line.Contains(":")))
                    {
                        // Skip if it's a parameter/property definition
                        if (line.Contains("string") || line.Contains("//") || line.Contains("todo"))
                            continue;

                        findings.Add(new AgentFinding
                        {
                            Id = Guid.NewGuid().ToString(),
                            FilePath = path,
                            LineRange = new AgentLineRange { Start = i + 1, End = i + 1 },
                            Category = "Security",
                            Severity = "High",
                            Description = $"Potential hardcoded {pattern} detected",
                            Explanation = "Hardcoded credentials or secrets should be moved to environment variables or a secrets manager.",
                            SuggestedFix = "Use environment variables or Azure Key Vault for sensitive configuration."
                        });
                        break;
                    }
                }
            }
        }

        return findings.Take(10).ToList(); // Limit mock findings
    }

    private static List<AgentFinding> GenerateMockArchitectureFindings(List<string> files)
    {
        var findings = new List<AgentFinding>();

        // Check for separation of concerns
        var hasLayers = files.Any(f => f.Contains("/Domain/") || f.Contains("\\Domain\\")) &&
                        files.Any(f => f.Contains("/Application/") || f.Contains("\\Application\\")) &&
                        files.Any(f => f.Contains("/Infrastructure/") || f.Contains("\\Infrastructure\\"));

        if (!hasLayers && files.Count > 10)
        {
            findings.Add(new AgentFinding
            {
                Id = Guid.NewGuid().ToString(),
                Category = "Architecture",
                Severity = "Medium",
                Description = "Consider implementing Clean Architecture layers",
                Explanation = "Separating code into Domain, Application, and Infrastructure layers improves maintainability.",
                SuggestedFix = "Organize code into separate projects for Domain, Application, Infrastructure, and API layers."
            });
        }

        // Check for dependency injection
        if (!files.Any(f => f.Contains("DependencyInjection") || f.Contains("ServiceCollection")))
        {
            findings.Add(new AgentFinding
            {
                Id = Guid.NewGuid().ToString(),
                Category = "Architecture",
                Severity = "Low",
                Description = "No explicit dependency injection configuration found",
                Explanation = "Using dependency injection improves testability and maintainability.",
                SuggestedFix = "Add a DependencyInjection.cs file to configure services."
            });
        }

        return findings;
    }

    private static List<string> GenerateMockRecommendations(AgentType agentType)
    {
        return agentType switch
        {
            AgentType.Structure => [
                "Consider adding a docs/ folder for documentation",
                "Ensure consistent file naming conventions",
                "Add a .editorconfig file for code style consistency"
            ],
            AgentType.CodeQuality => [
                "Enable nullable reference types across all projects",
                "Consider adding XML documentation for public APIs",
                "Use consistent naming conventions (PascalCase for public members)"
            ],
            AgentType.Security => [
                "Never commit secrets to source control",
                "Use environment variables for configuration",
                "Enable HTTPS for all endpoints",
                "Implement input validation on all API endpoints"
            ],
            AgentType.ArchitectureAdvisor => [
                "Follow SOLID principles for better maintainability",
                "Use dependency injection for loose coupling",
                "Consider using the Repository pattern for data access",
                "Implement the Result pattern for error handling"
            ],
            _ => []
        };
    }

    private static string GenerateMockSummary(AnalysisContext context, IEnumerable<AgentAnalysisResponse> agentResponses)
    {
        var totalFindings = agentResponses.Sum(r => r.Findings.Count);
        return $"[MOCK] Analysis completed for project '{context.ProjectName}'. Total findings: {totalFindings}";
    }

    private static string? ExtractJson(string text)
    {
        // Try to find JSON in the response
        var startIndex = text.IndexOf('{');
        var endIndex = text.LastIndexOf('}');
        
        if (startIndex >= 0 && endIndex > startIndex)
        {
            return text.Substring(startIndex, endIndex - startIndex + 1);
        }

        // Try to find JSON array
        startIndex = text.IndexOf('[');
        endIndex = text.LastIndexOf(']');
        
        if (startIndex >= 0 && endIndex > startIndex)
        {
            // Wrap in object
            var arrayContent = text.Substring(startIndex, endIndex - startIndex + 1);
            return $"{{\"findings\":{arrayContent},\"summary\":\"Analysis complete\",\"recommendations\":[]}}";
        }

        return null;
    }
}

/// <summary>
/// Configuration options for Azure OpenAI
/// </summary>
public class AzureOpenAiOptions
{
    /// <summary>
    /// Azure OpenAI endpoint URL
    /// TODO: Set via environment variable AZURE_OPENAI_ENDPOINT
    /// Example: https://your-resource.openai.azure.com/
    /// </summary>
    public string? Endpoint { get; set; }
    
    /// <summary>
    /// Azure OpenAI API key
    /// TODO: Set via environment variable AZURE_OPENAI_KEY
    /// </summary>
    public string? ApiKey { get; set; }
    
    /// <summary>
    /// Model deployment name
    /// Default: gpt-4
    /// </summary>
    public string? DeploymentName { get; set; } = "gpt-4";
}
