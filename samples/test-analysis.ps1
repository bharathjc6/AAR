# =============================================================================
# AAR Full Analysis Test Script
# Tests project creation AND report generation with mock OpenAI
# =============================================================================

param(
    [string]$BaseUrl = "http://localhost:5000",
    [string]$ApiKey = "aar_development_key_for_testing_only"
)

$ErrorActionPreference = "Stop"

Write-Host "============================================" -ForegroundColor Cyan
Write-Host " AAR Full Analysis Test (Mock OpenAI Mode)" -ForegroundColor Cyan
Write-Host "============================================" -ForegroundColor Cyan
Write-Host ""

# Common headers
$headers = @{
    "X-API-Key" = $ApiKey
}

# -----------------------------------------------------------------------------
# Step 1: Create a test project ZIP
# -----------------------------------------------------------------------------
Write-Host "Step 1: Creating test project ZIP..." -ForegroundColor Yellow

$testDir = Join-Path $env:TEMP "aar-test-$(Get-Random)"
New-Item -ItemType Directory -Path $testDir -Force | Out-Null

# Create sample files
$srcDir = Join-Path $testDir "src"
New-Item -ItemType Directory -Path $srcDir -Force | Out-Null

# Sample C# file with some mock findings to detect
@"
using System;

namespace TestProject
{
    // TODO: Add proper documentation
    public class Program
    {
        // This is a sample class for testing
        private static string connectionString = "Server=localhost;Database=test;Password=secret123";  // Security issue
        
        public static void Main(string[] args)
        {
            Console.WriteLine("Hello, World!");
            
            // Some code that could be improved
            if (args != null)
            {
                if (args.Length > 0)
                {
                    if (args[0] != null)
                    {
                        Console.WriteLine(args[0]);
                    }
                }
            }
        }
        
        public void VeryLongMethod()
        {
            // Simulating a long method
            var x = 1;
            var y = 2;
            var z = x + y;
            Console.WriteLine(z);
            // ... imagine 100+ more lines
        }
    }
}
"@ | Out-File -FilePath (Join-Path $srcDir "Program.cs") -Encoding UTF8

# Create a project file
@"
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
  </PropertyGroup>
</Project>
"@ | Out-File -FilePath (Join-Path $srcDir "TestProject.csproj") -Encoding UTF8

# Create README
@"
# Test Project

This is a sample project for AAR analysis testing.

## Features
- Sample code with intentional issues for detection
- Tests the mock OpenAI analysis mode
"@ | Out-File -FilePath (Join-Path $testDir "README.md") -Encoding UTF8

# Create the ZIP file
$zipPath = Join-Path $env:TEMP "aar-test-project.zip"
if (Test-Path $zipPath) { Remove-Item $zipPath -Force }
Compress-Archive -Path "$testDir\*" -DestinationPath $zipPath -Force

Write-Host "  Created test ZIP at: $zipPath" -ForegroundColor Green
Write-Host ""

# -----------------------------------------------------------------------------
# Step 2: Upload the project
# -----------------------------------------------------------------------------
Write-Host "Step 2: Uploading project for analysis..." -ForegroundColor Yellow

try {
    $boundary = [System.Guid]::NewGuid().ToString()
    $LF = "`r`n"
    
    $fileBytes = [System.IO.File]::ReadAllBytes($zipPath)
    $fileEnc = [System.Text.Encoding]::GetEncoding("ISO-8859-1").GetString($fileBytes)
    
    $bodyLines = @(
        "--$boundary",
        "Content-Disposition: form-data; name=`"name`"$LF",
        "Test Project $(Get-Date -Format 'HH:mm:ss')",
        "--$boundary",
        "Content-Disposition: form-data; name=`"description`"$LF",
        "Testing mock OpenAI analysis",
        "--$boundary",
        "Content-Disposition: form-data; name=`"file`"; filename=`"test-project.zip`"",
        "Content-Type: application/zip$LF",
        $fileEnc,
        "--$boundary--$LF"
    ) -join $LF
    
    $uploadHeaders = @{
        "X-API-Key" = $ApiKey
        "Content-Type" = "multipart/form-data; boundary=$boundary"
    }
    
    $response = Invoke-RestMethod -Uri "$BaseUrl/api/v1/projects" -Method Post -Headers $uploadHeaders -Body $bodyLines
    
    $projectId = $response.projectId
    Write-Host "  Project created! ID: $projectId" -ForegroundColor Green
    Write-Host "  Status: $($response.status)" -ForegroundColor Cyan
    Write-Host ""
}
catch {
    Write-Host "  ERROR: Failed to create project" -ForegroundColor Red
    Write-Host "  $($_.Exception.Message)" -ForegroundColor Red
    
    # Cleanup
    Remove-Item $testDir -Recurse -Force -ErrorAction SilentlyContinue
    Remove-Item $zipPath -Force -ErrorAction SilentlyContinue
    exit 1
}

# -----------------------------------------------------------------------------
# Step 3: Wait for analysis to complete
# -----------------------------------------------------------------------------
Write-Host "Step 3: Waiting for analysis to complete..." -ForegroundColor Yellow
Write-Host "  (The Worker service processes the queue and runs AI agents)" -ForegroundColor Gray
Write-Host ""

$maxWaitSeconds = 120
$waitInterval = 5
$waited = 0
$analysisComplete = $false

while ($waited -lt $maxWaitSeconds) {
    Start-Sleep -Seconds $waitInterval
    $waited += $waitInterval
    
    try {
        $project = Invoke-RestMethod -Uri "$BaseUrl/api/v1/projects/$projectId" -Method Get -Headers $headers
        $status = $project.status
        
        Write-Host "  [$waited s] Project status: $status" -ForegroundColor Cyan
        
        if ($status -eq "Completed") {
            $analysisComplete = $true
            Write-Host "  Analysis completed!" -ForegroundColor Green
            break
        }
        elseif ($status -eq "Failed") {
            Write-Host "  Analysis failed!" -ForegroundColor Red
            break
        }
    }
    catch {
        Write-Host "  [$waited s] Checking status..." -ForegroundColor Gray
    }
}

if (-not $analysisComplete) {
    Write-Host ""
    Write-Host "  WARNING: Analysis did not complete within $maxWaitSeconds seconds" -ForegroundColor Yellow
    Write-Host "  Make sure the Worker service is running:" -ForegroundColor Yellow
    Write-Host "    cd src\AAR.Worker && dotnet run" -ForegroundColor Cyan
    Write-Host ""
}

# -----------------------------------------------------------------------------
# Step 4: Fetch the report
# -----------------------------------------------------------------------------
Write-Host ""
Write-Host "Step 4: Fetching analysis report..." -ForegroundColor Yellow

try {
    $report = Invoke-RestMethod -Uri "$BaseUrl/api/v1/projects/$projectId/report" -Method Get -Headers $headers
    
    Write-Host ""
    Write-Host "  ========================================" -ForegroundColor Green
    Write-Host "  ANALYSIS REPORT" -ForegroundColor Green
    Write-Host "  ========================================" -ForegroundColor Green
    Write-Host ""
    Write-Host "  Report ID: $($report.id)" -ForegroundColor Cyan
    Write-Host "  Created: $($report.createdAt)" -ForegroundColor Cyan
    Write-Host "  Summary: $($report.summary)" -ForegroundColor Cyan
    Write-Host ""
    
    # Display findings by category
    if ($report.findings -and $report.findings.Count -gt 0) {
        Write-Host "  FINDINGS ($($report.findings.Count) total):" -ForegroundColor Yellow
        Write-Host "  ----------------------------------------" -ForegroundColor Yellow
        
        $grouped = $report.findings | Group-Object -Property category
        foreach ($group in $grouped) {
            Write-Host ""
            Write-Host "  [$($group.Name)] ($($group.Count) findings)" -ForegroundColor Magenta
            
            foreach ($finding in $group.Group | Select-Object -First 3) {
                $severityColor = switch ($finding.severity) {
                    "Critical" { "Red" }
                    "High" { "Red" }
                    "Medium" { "Yellow" }
                    "Low" { "Cyan" }
                    default { "Gray" }
                }
                
                Write-Host "    - [$($finding.severity)] $($finding.description)" -ForegroundColor $severityColor
                if ($finding.filePath) {
                    Write-Host "      File: $($finding.filePath)" -ForegroundColor Gray
                }
            }
            
            if ($group.Count -gt 3) {
                Write-Host "    ... and $($group.Count - 3) more" -ForegroundColor Gray
            }
        }
    }
    else {
        Write-Host "  No findings in the report." -ForegroundColor Gray
    }
    
    # Display recommendations
    if ($report.recommendations -and $report.recommendations.Count -gt 0) {
        Write-Host ""
        Write-Host "  RECOMMENDATIONS:" -ForegroundColor Yellow
        Write-Host "  ----------------------------------------" -ForegroundColor Yellow
        foreach ($rec in $report.recommendations | Select-Object -First 5) {
            Write-Host "    - $rec" -ForegroundColor Cyan
        }
    }
    
    Write-Host ""
}
catch {
    $statusCode = $_.Exception.Response.StatusCode.value__
    
    if ($statusCode -eq 202) {
        Write-Host "  Report not ready yet (202 Accepted)" -ForegroundColor Yellow
        Write-Host "  The Worker may still be processing." -ForegroundColor Yellow
    }
    elseif ($statusCode -eq 404) {
        Write-Host "  Report not found (404)" -ForegroundColor Red
        Write-Host "  The analysis may not have run. Check if Worker is running." -ForegroundColor Red
    }
    else {
        Write-Host "  ERROR: $($_.Exception.Message)" -ForegroundColor Red
    }
}

# -----------------------------------------------------------------------------
# Step 5: Download report as JSON
# -----------------------------------------------------------------------------
Write-Host ""
Write-Host "Step 5: Downloading report as JSON file..." -ForegroundColor Yellow

try {
    $jsonPath = Join-Path $env:TEMP "aar-report-$projectId.json"
    Invoke-RestMethod -Uri "$BaseUrl/api/v1/projects/$projectId/report/json" -Method Get -Headers $headers -OutFile $jsonPath
    Write-Host "  Saved to: $jsonPath" -ForegroundColor Green
}
catch {
    Write-Host "  Could not download JSON report" -ForegroundColor Yellow
}

# -----------------------------------------------------------------------------
# Cleanup
# -----------------------------------------------------------------------------
Write-Host ""
Write-Host "Cleanup..." -ForegroundColor Gray
Remove-Item $testDir -Recurse -Force -ErrorAction SilentlyContinue
Remove-Item $zipPath -Force -ErrorAction SilentlyContinue

# -----------------------------------------------------------------------------
# Summary
# -----------------------------------------------------------------------------
Write-Host ""
Write-Host "============================================" -ForegroundColor Cyan
Write-Host " Test Complete!" -ForegroundColor Cyan
Write-Host "============================================" -ForegroundColor Cyan
Write-Host ""
Write-Host "To run a full analysis test:" -ForegroundColor Yellow
Write-Host ""
Write-Host "  1. Start the API (Terminal 1):" -ForegroundColor Cyan
Write-Host "     cd src\AAR.Api && dotnet run" -ForegroundColor White
Write-Host ""
Write-Host "  2. Start the Worker (Terminal 2):" -ForegroundColor Cyan
Write-Host "     cd src\AAR.Worker && dotnet run" -ForegroundColor White
Write-Host ""
Write-Host "  3. Run this test script (Terminal 3):" -ForegroundColor Cyan
Write-Host "     .\samples\test-analysis.ps1" -ForegroundColor White
Write-Host ""
Write-Host "The mock OpenAI service will generate sample findings for:" -ForegroundColor Gray
Write-Host "  - Structure analysis (file organization)" -ForegroundColor Gray
Write-Host "  - Code quality (TODOs, long methods)" -ForegroundColor Gray
Write-Host "  - Security (hardcoded secrets)" -ForegroundColor Gray
Write-Host "  - Architecture (patterns, layers)" -ForegroundColor Gray
Write-Host ""
