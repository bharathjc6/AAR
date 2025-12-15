# =============================================================================
# setup-ollama-models.ps1
# Script to pull required Ollama models for local development and testing
# =============================================================================

param(
    [string]$OllamaUrl = "http://localhost:11434"
)

Write-Host "==============================================================================" -ForegroundColor Cyan
Write-Host "AAR - Ollama Model Setup" -ForegroundColor Cyan
Write-Host "==============================================================================" -ForegroundColor Cyan
Write-Host ""

# Check if Ollama is running
Write-Host "Checking Ollama availability at $OllamaUrl..." -ForegroundColor Yellow
try {
    $response = Invoke-RestMethod -Uri "$OllamaUrl/api/tags" -Method Get -TimeoutSec 5
    Write-Host "Ollama is running" -ForegroundColor Green
} catch {
    Write-Host "Ollama is not accessible at $OllamaUrl" -ForegroundColor Red
    Write-Host "Please ensure Ollama is running:" -ForegroundColor Red
    Write-Host "  - Docker: docker-compose up -d ollama" -ForegroundColor White
    Write-Host "  - Local: ollama serve" -ForegroundColor White
    exit 1
}

Write-Host ""

# Models required by AAR
$models = @(
    @{
        Name = "qwen2.5-coder:7b"
        Description = "LLM for code analysis"
        Size = "4.7GB"
    },
    @{
        Name = "bge-large:latest"
        Description = "Embedding model with 1024 dimensions"
        Size = "670MB"
    }
)

Write-Host "Models to be downloaded:" -ForegroundColor Yellow
foreach ($model in $models) {
    Write-Host "  - $($model.Name) - $($model.Description) [$($model.Size)]" -ForegroundColor White
}
Write-Host ""

# Check which models are already available
Write-Host "Checking existing models..." -ForegroundColor Yellow
$existingModels = $response.models | ForEach-Object { $_.name }
Write-Host ""

foreach ($model in $models) {
    $modelName = $model.Name
    
    if ($existingModels -contains $modelName) {
        Write-Host "Model '$modelName' already exists" -ForegroundColor Green
        continue
    }
    
    Write-Host "Pulling model: $modelName" -ForegroundColor Cyan
    Write-Host "  Description: $($model.Description)" -ForegroundColor Gray
    Write-Host "  Size: $($model.Size)" -ForegroundColor Gray
    Write-Host "  This may take several minutes..." -ForegroundColor Gray
    Write-Host ""
    
    try {
        # Pull model using ollama CLI
        $pullCommand = "ollama pull $modelName"
        
        if ($OllamaUrl -ne "http://localhost:11434") {
            # Set OLLAMA_HOST environment variable for non-default URLs
            $env:OLLAMA_HOST = $OllamaUrl
        }
        
        Invoke-Expression $pullCommand
        
        if ($LASTEXITCODE -eq 0) {
            Write-Host "Successfully pulled $modelName" -ForegroundColor Green
        } else {
            Write-Host "Failed to pull $modelName" -ForegroundColor Red
            exit 1
        }
    } catch {
        Write-Host "Error pulling $modelName : $_" -ForegroundColor Red
        exit 1
    }
    
    Write-Host ""
}

Write-Host "==============================================================================" -ForegroundColor Cyan
Write-Host "All models are ready!" -ForegroundColor Green
Write-Host "==============================================================================" -ForegroundColor Cyan
Write-Host ""

# Verify models
Write-Host "Verifying models..." -ForegroundColor Yellow
$response = Invoke-RestMethod -Uri "$OllamaUrl/api/tags" -Method Get
$availableModels = $response.models | ForEach-Object { $_.name }

foreach ($model in $models) {
    if ($availableModels -contains $model.Name) {
        Write-Host "  - $($model.Name) is available" -ForegroundColor Green
    } else {
        Write-Host "  - $($model.Name) NOT FOUND" -ForegroundColor Red
    }
}

Write-Host ""
Write-Host "You can now run integration and e2e tests with local AI services!" -ForegroundColor Cyan
Write-Host ""
