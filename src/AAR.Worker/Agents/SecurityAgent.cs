using System.Text.Json;
using System.Text.RegularExpressions;
using AAR.Application.Interfaces;
using AAR.Domain.Entities;
using AAR.Domain.Enums;
using AAR.Domain.ValueObjects;
using Microsoft.Extensions.Logging;

namespace AAR.Worker.Agents;

/// <summary>
/// Analyzes code for security vulnerabilities and best practices.
/// </summary>
public class SecurityAgent : BaseAgent
{
    public override AgentType AgentType => AgentType.Security;

    private static readonly Dictionary<string, (string Description, Severity Severity)> SecurityPatterns = new()
    {
        // SQL Injection
        [@"(""SELECT|'SELECT|""INSERT|'INSERT|""UPDATE|'UPDATE|""DELETE|'DELETE).*\+.*\w+"] = 
            ("Potential SQL Injection vulnerability - string concatenation in SQL query", Severity.Critical),
        
        // Hardcoded credentials
        [@"(password|passwd|pwd|secret|api[_-]?key|apikey|token|auth)\s*[=:]\s*[""'][^""']+[""']"] = 
            ("Potential hardcoded credential or secret", Severity.Critical),
        
        // Weak cryptography
        [@"\b(MD5|SHA1|DES)\b"] = 
            ("Use of weak cryptographic algorithm", Severity.High),
        
        // Insecure randomness
        [@"new\s+Random\s*\(\s*\)"] = 
            ("Insecure random number generator - use cryptographic RNG for security", Severity.Medium),
        
        // Path traversal
        [@"Path\.(Combine|Join).*Request\.(Query|Form|Path)"] = 
            ("Potential path traversal vulnerability", Severity.High),
        
        // Command injection
        [@"Process\.Start\s*\(.*\+"] = 
            ("Potential command injection - dynamic process execution", Severity.Critical),
        
        // XSS (basic patterns)
        [@"innerHTML\s*=|document\.write\(|\.html\("] = 
            ("Potential XSS vulnerability - unsafe HTML manipulation", Severity.High),
        
        // Insecure deserialization
        [@"BinaryFormatter|NetDataContractSerializer|ObjectStateFormatter"] = 
            ("Insecure deserialization - vulnerable to remote code execution", Severity.Critical),
        
        // Debug code in production
        [@"console\.log\(|Debug\.WriteLine|debugger;"] = 
            ("Debug code that should be removed in production", Severity.Low),
        
        // Disabled security
        [@"verify\s*=\s*false|ssl[_-]?verify\s*=\s*false|checkServerIdentity.*null"] = 
            ("SSL/TLS verification disabled", Severity.Critical),
        
        // Exposed endpoints
        [@"\[AllowAnonymous\]|\[Authorize\s*\(\s*\)\s*\]"] = 
            ("Review authentication configuration on endpoints", Severity.Info),
    };

    public SecurityAgent(
        IOpenAiService openAiService,
        ICodeMetricsService metricsService,
        ILogger<SecurityAgent> logger)
        : base(openAiService, metricsService, logger)
    {
    }

    public override async Task<List<ReviewFinding>> AnalyzeAsync(
        Guid projectId,
        string workingDirectory,
        CancellationToken cancellationToken = default)
    {
        Logger.LogInformation("SecurityAgent analyzing project {ProjectId}", projectId);
        
        var findings = new List<ReviewFinding>();
        var sourceFiles = GetSourceFiles(workingDirectory).ToList();
        
        try
        {
            // Pattern-based scanning
            foreach (var file in sourceFiles)
            {
                if (cancellationToken.IsCancellationRequested) break;
                
                var patternFindings = await ScanFileForPatternsAsync(projectId, file, workingDirectory, cancellationToken);
                findings.AddRange(patternFindings);
            }
            
            // Check for sensitive files
            findings.AddRange(CheckForSensitiveFiles(projectId, workingDirectory));
            
            // Check configuration files
            findings.AddRange(await CheckConfigurationFilesAsync(projectId, workingDirectory, cancellationToken));
            
            // Check dependencies for known vulnerabilities (basic)
            findings.AddRange(await CheckDependenciesAsync(projectId, workingDirectory, cancellationToken));
            
            // Sample files for AI analysis
            var criticalFiles = sourceFiles
                .Where(f => 
                    f.Contains("auth", StringComparison.OrdinalIgnoreCase) ||
                    f.Contains("security", StringComparison.OrdinalIgnoreCase) ||
                    f.Contains("login", StringComparison.OrdinalIgnoreCase) ||
                    f.Contains("password", StringComparison.OrdinalIgnoreCase) ||
                    f.Contains("crypto", StringComparison.OrdinalIgnoreCase) ||
                    f.Contains("controller", StringComparison.OrdinalIgnoreCase))
                .Take(5)
                .ToList();
            
            foreach (var file in criticalFiles)
            {
                if (cancellationToken.IsCancellationRequested) break;
                
                var content = await ReadFileContentAsync(file);
                var relativePath = Path.GetRelativePath(workingDirectory, file);
                
                var aiFindings = await AnalyzeWithAiAsync(projectId, relativePath, content, cancellationToken);
                findings.AddRange(aiFindings);
            }
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error in SecurityAgent analysis");
            findings.Add(CreateFinding(
                projectId,
                "Analysis Error",
                $"Security analysis encountered an error: {ex.Message}",
                Severity.Info,
                FindingCategory.Security
            ));
        }
        
        Logger.LogInformation("SecurityAgent found {Count} findings", findings.Count);
        return findings;
    }

    private async Task<List<ReviewFinding>> ScanFileForPatternsAsync(
        Guid projectId,
        string filePath,
        string workingDirectory,
        CancellationToken cancellationToken)
    {
        var findings = new List<ReviewFinding>();
        var content = await File.ReadAllTextAsync(filePath, cancellationToken);
        var lines = content.Split('\n');
        var relativePath = Path.GetRelativePath(workingDirectory, filePath);
        
        foreach (var (pattern, (description, severity)) in SecurityPatterns)
        {
            try
            {
                var regex = new Regex(pattern, RegexOptions.IgnoreCase | RegexOptions.Multiline);
                var matches = regex.Matches(content);
                
                foreach (Match match in matches)
                {
                    var lineNumber = content.Substring(0, match.Index).Count(c => c == '\n') + 1;
                    var codeLine = lineNumber <= lines.Length ? lines[lineNumber - 1].Trim() : match.Value;
                    
                    // Skip if it looks like a test or example
                    if (relativePath.Contains("test", StringComparison.OrdinalIgnoreCase) ||
                        relativePath.Contains("example", StringComparison.OrdinalIgnoreCase) ||
                        relativePath.Contains("sample", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }
                    
                    findings.Add(CreateFinding(
                        projectId,
                        "Security Pattern Match",
                        description,
                        severity,
                        FindingCategory.Security,
                        filePath: relativePath,
                        lineRange: new LineRange(lineNumber, lineNumber),
                        codeSnippet: codeLine.Length > 200 ? codeLine.Substring(0, 200) + "..." : codeLine,
                        suggestion: GetSuggestionForPattern(pattern)
                    ));
                }
            }
            catch (RegexMatchTimeoutException)
            {
                Logger.LogWarning("Regex timeout for pattern {Pattern} in file {File}", pattern, relativePath);
            }
        }
        
        return findings;
    }

    private string GetSuggestionForPattern(string pattern)
    {
        return pattern switch
        {
            var p when p.Contains("SELECT") || p.Contains("INSERT") => 
                "Use parameterized queries or an ORM to prevent SQL injection.",
            var p when p.Contains("password") || p.Contains("secret") =>
                "Move secrets to environment variables, Azure Key Vault, or a secrets manager.",
            var p when p.Contains("MD5") || p.Contains("SHA1") =>
                "Use SHA-256 or stronger for hashing, and bcrypt/Argon2 for passwords.",
            var p when p.Contains("Random") =>
                "Use RandomNumberGenerator for cryptographic purposes.",
            var p when p.Contains("Process.Start") =>
                "Validate and sanitize all inputs used in process execution.",
            var p when p.Contains("innerHTML") =>
                "Sanitize user input before rendering in HTML. Use textContent or proper encoding.",
            var p when p.Contains("BinaryFormatter") =>
                "Use JSON or XML serialization instead. Consider System.Text.Json or XmlSerializer.",
            _ => "Review this code for security implications."
        };
    }

    private List<ReviewFinding> CheckForSensitiveFiles(Guid projectId, string workingDirectory)
    {
        var findings = new List<ReviewFinding>();
        
        var sensitivePatterns = new[]
        {
            ("*.pem", "Private key file", Severity.Critical),
            ("*.key", "Private key file", Severity.Critical),
            ("*.pfx", "Certificate with private key", Severity.Critical),
            ("*.p12", "Certificate with private key", Severity.Critical),
            ("id_rsa", "SSH private key", Severity.Critical),
            ("id_ed25519", "SSH private key", Severity.Critical),
            (".env", "Environment file with potential secrets", Severity.High),
            ("*.env", "Environment file with potential secrets", Severity.High),
            ("secrets.json", "Secrets configuration file", Severity.High),
            ("appsettings.*.json", "Configuration file - check for secrets", Severity.Info),
            ("web.config", "Configuration file - check for connection strings", Severity.Info),
        };
        
        foreach (var (pattern, description, severity) in sensitivePatterns)
        {
            var files = Directory.GetFiles(workingDirectory, pattern, SearchOption.AllDirectories)
                .Where(f => !IsExcludedPath(f))
                .ToList();
            
            foreach (var file in files)
            {
                var relativePath = Path.GetRelativePath(workingDirectory, file);
                
                // Check if file is in .gitignore
                var isIgnored = CheckIfGitIgnored(workingDirectory, relativePath);
                
                findings.Add(CreateFinding(
                    projectId,
                    "Sensitive File Detected",
                    $"{description} found: {relativePath}",
                    isIgnored ? Severity.Info : severity,
                    FindingCategory.Security,
                    filePath: relativePath,
                    suggestion: isIgnored 
                        ? "File appears to be gitignored. Ensure it's not committed to version control."
                        : "Ensure this file is added to .gitignore and not committed to version control."
                ));
            }
        }
        
        return findings;
    }

    private bool CheckIfGitIgnored(string workingDirectory, string relativePath)
    {
        var gitignorePath = Path.Combine(workingDirectory, ".gitignore");
        
        if (!File.Exists(gitignorePath)) return false;
        
        try
        {
            var gitignoreContent = File.ReadAllText(gitignorePath);
            var fileName = Path.GetFileName(relativePath);
            
            return gitignoreContent.Contains(fileName) || 
                   gitignoreContent.Contains(relativePath);
        }
        catch
        {
            return false;
        }
    }

    private async Task<List<ReviewFinding>> CheckConfigurationFilesAsync(
        Guid projectId,
        string workingDirectory,
        CancellationToken cancellationToken)
    {
        var findings = new List<ReviewFinding>();
        
        var configPatterns = new[] { "*.json", "*.yaml", "*.yml", "*.xml", "*.config" };
        
        foreach (var pattern in configPatterns)
        {
            var files = Directory.GetFiles(workingDirectory, pattern, SearchOption.AllDirectories)
                .Where(f => !IsExcludedPath(f))
                .Take(20)
                .ToList();
            
            foreach (var file in files)
            {
                if (cancellationToken.IsCancellationRequested) break;
                
                var content = await File.ReadAllTextAsync(file, cancellationToken);
                var relativePath = Path.GetRelativePath(workingDirectory, file);
                
                // Check for hardcoded secrets in config
                var secretPatterns = new[]
                {
                    @"[""']?password[""']?\s*[:=]\s*[""'][^""']+[""']",
                    @"[""']?connectionstring[""']?\s*[:=].*password\s*=",
                    @"[""']?apikey[""']?\s*[:=]\s*[""'][^""']+[""']",
                    @"[""']?secret[""']?\s*[:=]\s*[""'][^""']+[""']",
                };
                
                foreach (var secretPattern in secretPatterns)
                {
                    var matches = Regex.Matches(content, secretPattern, RegexOptions.IgnoreCase);
                    
                    foreach (Match match in matches)
                    {
                        // Skip if it looks like a placeholder
                        if (match.Value.Contains("${") || 
                            match.Value.Contains("{{") || 
                            match.Value.Contains("YOUR_") ||
                            match.Value.Contains("REPLACE_"))
                        {
                            continue;
                        }
                        
                        var lineNumber = content.Substring(0, match.Index).Count(c => c == '\n') + 1;
                        
                        findings.Add(CreateFinding(
                            projectId,
                            "Potential Secret in Configuration",
                            "Configuration file may contain hardcoded secrets.",
                            Severity.High,
                            FindingCategory.Security,
                            filePath: relativePath,
                            lineRange: new LineRange(lineNumber, lineNumber),
                            suggestion: "Use environment variables, Azure Key Vault, or a secrets manager for sensitive values."
                        ));
                    }
                }
            }
        }
        
        return findings.Take(20).ToList();
    }

    private async Task<List<ReviewFinding>> CheckDependenciesAsync(
        Guid projectId,
        string workingDirectory,
        CancellationToken cancellationToken)
    {
        var findings = new List<ReviewFinding>();
        
        // Check package.json
        var packageJsonPath = Path.Combine(workingDirectory, "package.json");
        if (File.Exists(packageJsonPath))
        {
            findings.Add(CreateFinding(
                projectId,
                "Dependency Audit Recommended",
                "package.json found. Run 'npm audit' to check for known vulnerabilities.",
                Severity.Info,
                FindingCategory.Security,
                filePath: "package.json",
                suggestion: "Run 'npm audit' or 'npm audit fix' to check and fix vulnerable dependencies."
            ));
        }
        
        // Check for .csproj files
        var csprojFiles = Directory.GetFiles(workingDirectory, "*.csproj", SearchOption.AllDirectories);
        if (csprojFiles.Any())
        {
            findings.Add(CreateFinding(
                projectId,
                "Dependency Audit Recommended",
                ".NET project files found. Use 'dotnet list package --vulnerable' to check for vulnerabilities.",
                Severity.Info,
                FindingCategory.Security,
                suggestion: "Run 'dotnet list package --vulnerable' to identify packages with known vulnerabilities."
            ));
        }
        
        // Check requirements.txt
        var requirementsPath = Path.Combine(workingDirectory, "requirements.txt");
        if (File.Exists(requirementsPath))
        {
            findings.Add(CreateFinding(
                projectId,
                "Dependency Audit Recommended",
                "requirements.txt found. Use 'pip-audit' or 'safety check' to check for vulnerabilities.",
                Severity.Info,
                FindingCategory.Security,
                filePath: "requirements.txt",
                suggestion: "Install pip-audit and run 'pip-audit' to check for vulnerable Python packages."
            ));
        }
        
        return findings;
    }

    private async Task<List<ReviewFinding>> AnalyzeWithAiAsync(
        Guid projectId,
        string relativePath,
        string content,
        CancellationToken cancellationToken)
    {
        var findings = new List<ReviewFinding>();
        
        var prompt = $@"Analyze the following code for security vulnerabilities.
Focus on OWASP Top 10 categories:
1. Injection (SQL, Command, LDAP, etc.)
2. Broken Authentication
3. Sensitive Data Exposure
4. XML External Entities (XXE)
5. Broken Access Control
6. Security Misconfiguration
7. Cross-Site Scripting (XSS)
8. Insecure Deserialization
9. Using Components with Known Vulnerabilities
10. Insufficient Logging & Monitoring

File: {relativePath}

```
{content}
```

Respond with a JSON array of security findings (max 5 most critical):
[
  {{
    ""title"": ""Vulnerability title"",
    ""description"": ""Detailed description of the vulnerability"",
    ""severity"": ""Critical|High|Medium|Low|Info"",
    ""lineNumber"": 123,
    ""cweId"": ""CWE-XXX"",
    ""suggestion"": ""How to remediate""
  }}
]

Only respond with the JSON array. If no issues, respond with [].";

        try
        {
            var response = await OpenAiService.AnalyzeCodeAsync(prompt, "SecurityAgent", cancellationToken);
            
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
                            ? s : Severity.Medium;
                        
                        LineRange? lineRange = f.LineNumber > 0
                            ? new LineRange(f.LineNumber, f.LineNumber)
                            : null;
                        
                        var description = f.Description ?? "";
                        if (!string.IsNullOrEmpty(f.CweId))
                        {
                            description += $" ({f.CweId})";
                        }
                        
                        findings.Add(CreateFinding(
                            projectId,
                            f.Title ?? "Security Vulnerability",
                            description,
                            severity,
                            FindingCategory.Security,
                            filePath: relativePath,
                            lineRange: lineRange,
                            suggestion: f.Suggestion
                        ));
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Failed to analyze file {File} with AI for security", relativePath);
        }
        
        return findings;
    }

    private class AiFinding
    {
        public string? Title { get; set; }
        public string? Description { get; set; }
        public string? Severity { get; set; }
        public int LineNumber { get; set; }
        public string? CweId { get; set; }
        public string? Suggestion { get; set; }
    }
}
