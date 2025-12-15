# =============================================================================
# start-local-services.ps1
# Script to start and verify Docker, Ollama, and Qdrant services
# =============================================================================

Write-Host "==============================================================================" -ForegroundColor Cyan
Write-Host "AAR - Starting Local Services" -ForegroundColor Cyan
Write-Host "==============================================================================" -ForegroundColor Cyan
Write-Host ""

# Check if Docker is running
Write-Host "Checking Docker..." -ForegroundColor Yellow
try {
    $dockerInfo = docker info 2>&1
    if ($LASTEXITCODE -ne 0) {
        throw "Docker not running"
    }
    Write-Host "  Docker is running" -ForegroundColor Green
} catch {
    Write-Host "  Docker is not running" -ForegroundColor Red
    Write-Host "  Please start Docker Desktop and try again" -ForegroundColor Red
    exit 1
}

Write-Host ""

# Start Ollama and Qdrant services
Write-Host "Starting Ollama and Qdrant services..." -ForegroundColor Yellow
Write-Host "  Running: docker-compose up -d ollama qdrant" -ForegroundColor Gray
Write-Host ""

docker-compose up -d ollama qdrant

if ($LASTEXITCODE -ne 0) {
    Write-Host "  Failed to start services" -ForegroundColor Red
    exit 1
}

Write-Host ""
Write-Host "  Services started" -ForegroundColor Green
Write-Host ""

# Wait for services to be healthy
Write-Host "Waiting for services to be ready..." -ForegroundColor Yellow
Write-Host ""

# Check Qdrant
Write-Host "  Checking Qdrant (http://localhost:6333)..." -ForegroundColor Gray
$maxRetries = 30
$retryCount = 0
$qdrantReady = $false

while (-not $qdrantReady -and $retryCount -lt $maxRetries) {
    try {
        $response = Invoke-RestMethod -Uri "http://localhost:6333/health" -Method Get -TimeoutSec 2
        $qdrantReady = $true
        Write-Host "    Qdrant is ready" -ForegroundColor Green
    } catch {
        $retryCount++
        Write-Host "    Waiting... (attempt $retryCount/$maxRetries)" -ForegroundColor Gray
        Start-Sleep -Seconds 2
    }
}

if (-not $qdrantReady) {
    Write-Host "    Qdrant failed to start" -ForegroundColor Red
    Write-Host "      Check logs: docker logs aar-qdrant" -ForegroundColor Red
    exit 1
}

# Check Ollama
Write-Host "  Checking Ollama (http://localhost:11434)..." -ForegroundColor Gray
$retryCount = 0
$ollamaReady = $false

while (-not $ollamaReady -and $retryCount -lt $maxRetries) {
    try {
        $response = Invoke-RestMethod -Uri "http://localhost:11434/api/tags" -Method Get -TimeoutSec 2
        $ollamaReady = $true
        Write-Host "    Ollama is ready" -ForegroundColor Green
    } catch {
        $retryCount++
        Write-Host "    Waiting... (attempt $retryCount/$maxRetries)" -ForegroundColor Gray
        Start-Sleep -Seconds 2
    }
}

if (-not $ollamaReady) {
    Write-Host "    Ollama failed to start" -ForegroundColor Red
    Write-Host "      Check logs: docker logs aar-ollama" -ForegroundColor Red
    exit 1
}

Write-Host ""
Write-Host "==============================================================================" -ForegroundColor Cyan
Write-Host "All services are ready!" -ForegroundColor Green
Write-Host "==============================================================================" -ForegroundColor Cyan
Write-Host ""

Write-Host "Service URLs:" -ForegroundColor Yellow
Write-Host "  - Qdrant UI:  http://localhost:6333/dashboard" -ForegroundColor White
Write-Host "  - Ollama API: http://localhost:11434" -ForegroundColor White
Write-Host ""

Write-Host "Next steps:" -ForegroundColor Yellow
Write-Host "  1. Pull Ollama models: .\scripts\setup-ollama-models.ps1" -ForegroundColor White
Write-Host "  2. Run tests: dotnet test" -ForegroundColor White
Write-Host "  3. Start API: docker-compose up -d api" -ForegroundColor White
Write-Host ""
