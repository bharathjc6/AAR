// =============================================================================
// AAR.Infrastructure - Services/PromptTemplateProvider.cs
// Provides prompt templates for analysis agents
// =============================================================================

using AAR.Application.DTOs;
using AAR.Domain.Enums;
using System.Text;

namespace AAR.Infrastructure.Services;

/// <summary>
/// Interface for providing prompt templates to agents
/// </summary>
public interface IPromptTemplateProvider
{
    string GetSystemPrompt(AgentType agentType);
    string GetUserPrompt(AgentType agentType, AnalysisContext context, IDictionary<string, string> fileContents);
}

/// <summary>
/// Provides prompt templates for analysis agents
/// </summary>
public class PromptTemplateProvider : IPromptTemplateProvider
{
    private const string JsonSchema = """
        {
            "findings": [
                {
                    "id": "uuid",
                    "filePath": "string (relative path)",
                    "lineRange": { "start": int, "end": int },
                    "category": "Performance|Security|Architecture|CodeQuality|Structure",
                    "severity": "High|Medium|Low",
                    "description": "string (short description)",
                    "explanation": "string (detailed explanation)",
                    "suggestedFix": "string",
                    "fixedCodeSnippet": "string (optional)",
                    "originalCodeSnippet": "string (optional)"
                }
            ],
            "summary": "string",
            "recommendations": ["string"]
        }
        """;

    /// <inheritdoc/>
    public string GetSystemPrompt(AgentType agentType)
    {
        return agentType switch
        {
            AgentType.Structure => GetStructureAgentSystemPrompt(),
            AgentType.CodeQuality => GetCodeQualityAgentSystemPrompt(),
            AgentType.Security => GetSecurityAgentSystemPrompt(),
            AgentType.ArchitectureAdvisor => GetArchitectureAdvisorSystemPrompt(),
            _ => throw new ArgumentOutOfRangeException(nameof(agentType))
        };
    }

    /// <inheritdoc/>
    public string GetUserPrompt(AgentType agentType, AnalysisContext context, IDictionary<string, string> fileContents)
    {
        var sb = new StringBuilder();
        
        sb.AppendLine($"## Project: {context.ProjectName}");
        sb.AppendLine($"## Total Files: {context.Files.Count}");
        sb.AppendLine();
        sb.AppendLine("## Files to Analyze:");
        sb.AppendLine();

        foreach (var (path, content) in fileContents)
        {
            sb.AppendLine($"### File: {path}");
            sb.AppendLine("```");
            // Truncate very large files
            if (content.Length > 10000)
            {
                sb.AppendLine(content[..10000]);
                sb.AppendLine("... (truncated)");
            }
            else
            {
                sb.AppendLine(content);
            }
            sb.AppendLine("```");
            sb.AppendLine();
        }

        sb.AppendLine("## Instructions:");
        sb.AppendLine(GetAgentSpecificInstructions(agentType));
        sb.AppendLine();
        sb.AppendLine("Return your analysis as valid JSON matching the schema provided in the system prompt.");

        return sb.ToString();
    }

    private static string GetStructureAgentSystemPrompt()
    {
        return $"""
            You are a Structure Analysis Agent specialized in reviewing .NET project organization and file structure.
            
            Your responsibilities:
            1. Analyze project folder structure and organization
            2. Check for proper separation of concerns (layers, projects)
            3. Verify naming conventions for files and folders
            4. Identify missing essential files (README, .gitignore, etc.)
            5. Check solution and project file organization
            6. Evaluate namespace organization
            
            Focus on:
            - Clean Architecture compliance (Domain, Application, Infrastructure, API layers)
            - Proper project references and dependencies
            - Consistent naming patterns
            - Documentation presence
            - Configuration file organization
            
            Return your findings as valid JSON matching this schema:
            {JsonSchema}
            
            Be specific and actionable in your recommendations. Only report genuine issues.
            """;
    }

    private static string GetCodeQualityAgentSystemPrompt()
    {
        return $"""
            You are a Code Quality Agent specialized in reviewing .NET/C# code for quality issues.
            
            Your responsibilities:
            1. Check for code readability and maintainability
            2. Identify code smells and anti-patterns
            3. Review naming conventions (PascalCase for public, camelCase for private)
            4. Check for proper async/await usage
            5. Identify overly complex methods (high cyclomatic complexity)
            6. Review exception handling practices
            7. Check for proper use of LINQ and modern C# features
            8. Identify code duplication opportunities
            
            Focus on:
            - Method length and complexity
            - Proper null handling (nullable reference types)
            - Resource disposal (IDisposable)
            - String handling best practices
            - Collection usage efficiency
            
            Return your findings as valid JSON matching this schema:
            {JsonSchema}
            
            Prioritize actionable findings. Include code snippets where helpful.
            """;
    }

    private static string GetSecurityAgentSystemPrompt()
    {
        return $"""
            You are a Security Agent specialized in identifying security vulnerabilities in .NET applications.
            
            Your responsibilities:
            1. Identify hardcoded secrets, passwords, and API keys
            2. Check for SQL injection vulnerabilities
            3. Review authentication and authorization implementation
            4. Check for cross-site scripting (XSS) vulnerabilities
            5. Identify insecure deserialization
            6. Review cryptography usage
            7. Check for path traversal vulnerabilities
            8. Review input validation
            9. Check for sensitive data exposure in logs
            10. Identify insecure direct object references
            
            Focus on OWASP Top 10 vulnerabilities and .NET-specific security concerns.
            
            Return your findings as valid JSON matching this schema:
            {JsonSchema}
            
            Security findings should be marked with appropriate severity:
            - High: Immediate security risk, exploitable
            - Medium: Potential security risk, needs attention
            - Low: Security best practice violation
            """;
    }

    private static string GetArchitectureAdvisorSystemPrompt()
    {
        return $"""
            You are an Architecture Advisor Agent specialized in reviewing .NET application architecture.
            
            Your responsibilities:
            1. Evaluate adherence to Clean Architecture principles
            2. Review dependency injection usage and configuration
            3. Check for SOLID principle violations
            4. Analyze domain model design
            5. Review API design and RESTful practices
            6. Check for proper abstraction layers
            7. Evaluate error handling strategy
            8. Review caching and performance patterns
            9. Check for proper configuration management
            10. Analyze testability of the codebase
            
            Focus on:
            - Dependency direction (dependencies should point inward)
            - Interface segregation
            - Single responsibility principle
            - Proper use of design patterns
            - Separation of concerns
            
            Return your findings as valid JSON matching this schema:
            {JsonSchema}
            
            Provide constructive architectural guidance with specific recommendations.
            """;
    }

    private static string GetAgentSpecificInstructions(AgentType agentType)
    {
        return agentType switch
        {
            AgentType.Structure => "Focus on project structure, file organization, and naming conventions.",
            AgentType.CodeQuality => "Focus on code quality, maintainability, and C# best practices.",
            AgentType.Security => "Focus on security vulnerabilities and sensitive data handling.",
            AgentType.ArchitectureAdvisor => "Focus on architectural patterns and design principles.",
            _ => "Analyze the code comprehensively."
        };
    }
}
