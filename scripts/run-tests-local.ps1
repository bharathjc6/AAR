# =============================================================================
# run-tests-local.ps1
# Script to run tests with local Ollama and Qdrant services
# =============================================================================

param(
    [string]$Filter = "",
    [switch]$Verbose = $false
)

Write-Host "==============================================================================" -ForegroundColor Cyan
Write-Host "AAR - Running Tests with Local AI Services" -ForegroundColor Cyan
Write-Host "==============================================================================" -ForegroundColor Cyan
Write-Host ""

# Check services are running
Write-Host "Checking required services..." -ForegroundColor Yellow

# Check Qdrant
try {
    $response = Invoke-RestMethod -Uri "http://localhost:6333/health" -Method Get -TimeoutSec 2
    Write-Host "  Qdrant: Running" -ForegroundColor Green
} catch {
    Write-Host "  Qdrant: NOT RUNNING" -ForegroundColor Red
    Write-Host "  Please start services: .\scripts\start-local-services.ps1" -ForegroundColor Red
    exit 1
}

# Check Ollama
try {
    $response = Invoke-RestMethod -Uri "http://localhost:11434/api/tags" -Method Get -TimeoutSec 2
    Write-Host "  Ollama: Running" -ForegroundColor Green
    
    # Check models
    $modelNames = $response.models | ForEach-Object { $_.name }
    if ($modelNames -contains "qwen2.5-coder:7b") {
        Write-Host "    - qwen2.5-coder:7b: Available" -ForegroundColor Green
    } else {
        Write-Host "    - qwen2.5-coder:7b: MISSING" -ForegroundColor Red
        Write-Host "  Please run: .\scripts\setup-ollama-models.ps1" -ForegroundColor Red
        exit 1
    }
    
    if ($modelNames -contains "bge-large:latest") {
        Write-Host "    - bge-large:latest: Available" -ForegroundColor Green
    } else {
        Write-Host "    - bge-large:latest: MISSING" -ForegroundColor Red
        Write-Host "  Please run: .\scripts\setup-ollama-models.ps1" -ForegroundColor Red
        exit 1
    }
} catch {
    Write-Host "  Ollama: NOT RUNNING" -ForegroundColor Red
    Write-Host "  Please start services: .\scripts\start-local-services.ps1" -ForegroundColor Red
    exit 1
}

Write-Host ""
Write-Host "All services are ready!" -ForegroundColor Green
Write-Host ""

# Build solution
Write-Host "Building solution..." -ForegroundColor Yellow
dotnet build AAR.sln --no-restore

if ($LASTEXITCODE -ne 0) {
    Write-Host "Build failed!" -ForegroundColor Red
    exit 1
}

Write-Host ""
Write-Host "==============================================================================" -ForegroundColor Cyan
Write-Host "Running Tests" -ForegroundColor Cyan
Write-Host "==============================================================================" -ForegroundColor Cyan
Write-Host ""

$testArgs = @("test", "tests/AAR.Tests/AAR.Tests.csproj", "--no-build")

if ($Verbose) {
    $testArgs += @("--logger", "console;verbosity=detailed")
} else {
    $testArgs += @("--logger", "console;verbosity=normal")
}

if ($Filter) {
    $testArgs += @("--filter", $Filter)
    Write-Host "Filter: $Filter" -ForegroundColor Gray
}

Write-Host ""

dotnet @testArgs

$testResult = $LASTEXITCODE

Write-Host ""
Write-Host "==============================================================================" -ForegroundColor Cyan

if ($testResult -eq 0) {
    Write-Host "All tests passed!" -ForegroundColor Green
} else {
    Write-Host "Some tests failed!" -ForegroundColor Red
}

Write-Host "==============================================================================" -ForegroundColor Cyan
Write-Host ""

exit $testResult
