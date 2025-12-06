// =============================================================================
// AAR.Shared - Error.cs
// Standardized error representation for the application
// =============================================================================

namespace AAR.Shared;

/// <summary>
/// Represents an error with a code and message for consistent error handling
/// </summary>
/// <param name="Code">Machine-readable error code</param>
/// <param name="Message">Human-readable error message</param>
public record Error(string Code, string Message)
{
    /// <summary>
    /// Additional details about the error (optional)
    /// </summary>
    public IDictionary<string, object>? Details { get; init; }

    /// <summary>
    /// Creates an error with additional details
    /// </summary>
    public Error WithDetails(IDictionary<string, object> details) =>
        this with { Details = details };
}

/// <summary>
/// Common domain errors used throughout the application
/// </summary>
public static class DomainErrors
{
    public static class Project
    {
        public static Error NotFound(Guid id) => 
            new("Project.NotFound", $"Project with ID '{id}' was not found.");
        
        public static Error InvalidZipFile => 
            new("Project.InvalidZipFile", "The uploaded file is not a valid ZIP archive.");
        
        public static Error InvalidGitUrl => 
            new("Project.InvalidGitUrl", "The provided Git repository URL is invalid.");
        
        public static Error FileTooLarge => 
            new("Project.FileTooLarge", "The uploaded file exceeds the maximum allowed size.");
        
        public static Error AlreadyAnalyzing => 
            new("Project.AlreadyAnalyzing", "The project is already being analyzed.");
        
        public static Error NoFilesToAnalyze => 
            new("Project.NoFilesToAnalyze", "The project contains no analyzable files.");
        
        public static Error AnalysisNotStarted => 
            new("Project.AnalysisNotStarted", "Analysis has not been started for this project.");
        
        public static Error PathTraversalDetected => 
            new("Project.PathTraversalDetected", "Path traversal attempt detected in file path.");
    }

    public static class Report
    {
        public static Error NotFound(Guid projectId) => 
            new("Report.NotFound", $"Report for project '{projectId}' was not found.");
        
        public static Error NotReady => 
            new("Report.NotReady", "The report is not yet ready. Analysis is still in progress.");
        
        public static Error GenerationFailed => 
            new("Report.GenerationFailed", "Failed to generate the report.");
    }

    public static class Authentication
    {
        public static Error InvalidApiKey => 
            new("Auth.InvalidApiKey", "The provided API key is invalid or expired.");
        
        public static Error MissingApiKey => 
            new("Auth.MissingApiKey", "API key is required for this operation.");
    }

    public static class OpenAI
    {
        public static Error ServiceUnavailable => 
            new("OpenAI.ServiceUnavailable", "The OpenAI service is currently unavailable.");
        
        public static Error InvalidResponse => 
            new("OpenAI.InvalidResponse", "The OpenAI service returned an invalid response.");
        
        public static Error RateLimited => 
            new("OpenAI.RateLimited", "Rate limit exceeded. Please try again later.");
    }

    public static class Queue
    {
        public static Error EnqueueFailed => 
            new("Queue.EnqueueFailed", "Failed to enqueue the analysis job.");
    }

    public static class Validation
    {
        public static Error InvalidRequest(string details) => 
            new("Validation.InvalidRequest", details);
    }
}
