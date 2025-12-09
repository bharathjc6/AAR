# =============================================================================
# AAR - Autonomous Architecture Reviewer - API Testing Script
# =============================================================================
# This script demonstrates how to interact with the AAR API
# =============================================================================

# Configuration
$BaseUrl = "http://localhost:5000"
$ApiKey = "vRk9ZqgyOFXy4NFoxbbRE32EOuhmE8WD"  # Development key (generated on first run)

# Headers for authenticated requests
$Headers = @{
    "X-API-KEY" = $ApiKey
}

# =============================================================================
# 1. HEALTH CHECK (No authentication required)
# =============================================================================
Write-Host "`n=== 1. Health Check ===" -ForegroundColor Cyan

$health = Invoke-RestMethod -Uri "$BaseUrl/health" -Method GET
Write-Host "Status: $($health.status)" -ForegroundColor Green
Write-Host "Database: $($health.entries.database.status)" -ForegroundColor Green

# =============================================================================
# 2. LIST ALL PROJECTS
# =============================================================================
Write-Host "`n=== 2. List All Projects ===" -ForegroundColor Cyan

$projects = Invoke-RestMethod -Uri "$BaseUrl/api/v1/projects" -Method GET -Headers $Headers
Write-Host "Total Projects: $($projects.totalCount)" -ForegroundColor Green

foreach ($project in $projects.items) {
    Write-Host "  - [$($project.statusText)] $($project.name) (ID: $($project.id))" -ForegroundColor White
}

# =============================================================================
# 3. CREATE A NEW PROJECT (from ZIP file)
# =============================================================================
Write-Host "`n=== 3. Create Project from ZIP ===" -ForegroundColor Cyan

# First, create a sample project to upload
$testDir = "C:\temp\aar-sample-project"
$zipPath = "C:\temp\sample-project.zip"

# Clean up
Remove-Item $testDir -Recurse -Force -ErrorAction SilentlyContinue
Remove-Item $zipPath -Force -ErrorAction SilentlyContinue

# Create sample project structure
New-Item -ItemType Directory -Path "$testDir\src" -Force | Out-Null

# Create sample C# files
@"
namespace SampleProject;

/// <summary>
/// Simple calculator class for demonstration
/// </summary>
public class Calculator
{
    public int Add(int a, int b) => a + b;
    public int Subtract(int a, int b) => a - b;
    public int Multiply(int a, int b) => a * b;
    
    public int Divide(int a, int b)
    {
        if (b == 0)
            throw new DivideByZeroException("Cannot divide by zero");
        return a / b;
    }
}
"@ | Set-Content -Path "$testDir\src\Calculator.cs" -Encoding UTF8

@"
namespace SampleProject;

public class Program
{
    public static void Main(string[] args)
    {
        var calc = new Calculator();
        
        Console.WriteLine("Calculator Demo");
        Console.WriteLine($"5 + 3 = {calc.Add(5, 3)}");
        Console.WriteLine($"10 - 4 = {calc.Subtract(10, 4)}");
        Console.WriteLine($"6 * 7 = {calc.Multiply(6, 7)}");
        Console.WriteLine($"20 / 4 = {calc.Divide(20, 4)}");
    }
}
"@ | Set-Content -Path "$testDir\src\Program.cs" -Encoding UTF8

# Create project file
@"
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
  </PropertyGroup>
</Project>
"@ | Set-Content -Path "$testDir\SampleProject.csproj" -Encoding UTF8

# Create zip file
Compress-Archive -Path "$testDir\*" -DestinationPath $zipPath -Force
Write-Host "Created sample project ZIP: $zipPath" -ForegroundColor Gray

# Upload using curl.exe (PowerShell's Invoke-RestMethod has issues with multipart/form-data)
Write-Host "Uploading project..." -ForegroundColor Gray

$result = & "C:\Windows\System32\curl.exe" -s -X POST "$BaseUrl/api/v1/projects" `
    -H "X-API-KEY: $ApiKey" `
    -F "name=Sample Calculator Project" `
    -F "description=A sample C# calculator project for AAR demo" `
    -F "file=@$zipPath" | ConvertFrom-Json

if ($result.projectId) {
    Write-Host "Project created successfully!" -ForegroundColor Green
    Write-Host "  Project ID: $($result.projectId)" -ForegroundColor White
    Write-Host "  Name: $($result.name)" -ForegroundColor White
    Write-Host "  Status: $($result.status)" -ForegroundColor White
    
    $ProjectId = $result.projectId
} else {
    Write-Host "Error creating project: $($result | ConvertTo-Json)" -ForegroundColor Red
    exit 1
}

# =============================================================================
# 4. GET PROJECT DETAILS
# =============================================================================
Write-Host "`n=== 4. Get Project Details ===" -ForegroundColor Cyan

$projectDetails = Invoke-RestMethod -Uri "$BaseUrl/api/v1/projects/$ProjectId" -Method GET -Headers $Headers
Write-Host "Name: $($projectDetails.name)" -ForegroundColor White
Write-Host "Description: $($projectDetails.description)" -ForegroundColor White
Write-Host "Status: $($projectDetails.statusText)" -ForegroundColor White
Write-Host "Created: $($projectDetails.createdAt)" -ForegroundColor White

# =============================================================================
# 5. START ANALYSIS
# =============================================================================
Write-Host "`n=== 5. Start Analysis ===" -ForegroundColor Cyan
Write-Host "Note: Analysis requires Azure OpenAI to be configured." -ForegroundColor Yellow
Write-Host "Without Azure OpenAI, the analysis will be queued but won't complete." -ForegroundColor Yellow

try {
    $analysisResult = Invoke-RestMethod -Uri "$BaseUrl/api/v1/projects/$ProjectId/analyze" `
        -Method POST -Headers $Headers
    
    Write-Host "Analysis started!" -ForegroundColor Green
    Write-Host "  Project ID: $($analysisResult.projectId)" -ForegroundColor White
    Write-Host "  Message: $($analysisResult.message)" -ForegroundColor White
} catch {
    $errorResponse = $_.ErrorDetails.Message | ConvertFrom-Json
    Write-Host "Error: $($errorResponse.error.message)" -ForegroundColor Red
}

# =============================================================================
# 6. GET REPORTS
# =============================================================================
Write-Host "`n=== 6. Get Reports ===" -ForegroundColor Cyan

try {
    $reports = Invoke-RestMethod -Uri "$BaseUrl/api/v1/reports" -Method GET -Headers $Headers
    Write-Host "Total Reports: $($reports.totalCount)" -ForegroundColor White

    foreach ($report in $reports.items) {
        Write-Host "  - Report ID: $($report.id)" -ForegroundColor Gray
        Write-Host "    Health Score: $($report.healthScore)" -ForegroundColor White
        Write-Host "    Findings: High=$($report.highSeverityCount), Medium=$($report.mediumSeverityCount), Low=$($report.lowSeverityCount)" -ForegroundColor White
    }
} catch {
    Write-Host "No reports available yet." -ForegroundColor Yellow
    Write-Host "Reports are generated after analysis completes (requires Azure OpenAI)." -ForegroundColor Gray
}

# =============================================================================
# SUMMARY
# =============================================================================
Write-Host "`n=== Testing Complete ===" -ForegroundColor Cyan
Write-Host @"

Available Endpoints:
  Health:
    GET  /health              - Health check (no auth required)
    GET  /health/ready        - Readiness check

  Projects:
    GET  /api/v1/projects           - List all projects (paginated)
    POST /api/v1/projects           - Create project from ZIP (multipart/form-data)
    POST /api/v1/projects/git       - Create project from Git URL
    GET  /api/v1/projects/{id}      - Get project details
    POST /api/v1/projects/{id}/analyze - Start analysis

  Reports:
    GET  /api/v1/reports            - List all reports (paginated)
    GET  /api/v1/reports/{id}       - Get report details
    GET  /api/v1/reports/{id}/pdf   - Download report as PDF
    GET  /api/v1/reports/project/{projectId} - Get report by project ID

Authentication:
  All endpoints except /health require the X-API-KEY header.
  Development API Key: $ApiKey

Swagger UI:
  Open http://localhost:5000 in your browser to explore the API interactively.

"@ -ForegroundColor Gray
