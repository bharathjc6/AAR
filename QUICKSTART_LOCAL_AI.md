# Quick Start - Local AI Stack

## Prerequisites Installation (One-Time Setup)

### 1. Install Ollama
```powershell
# Windows
winget install Ollama.Ollama

# Verify installation
ollama --version
```

### 2. Pull AI Models
```bash
# LLM Model (~4.7GB, takes 5-10 minutes)
ollama pull qwen2.5-coder:7b

# Embedding Model (~600MB, takes 1-2 minutes)
ollama pull bge-large-en-v1.5

# Verify models
ollama list
```

### 3. Install Docker Desktop
```powershell
# Windows
winget install Docker.DockerDesktop

# Restart computer after installation
```

### 4. Start Qdrant Vector Database
```bash
# Pull and run Qdrant container
docker run -d --name qdrant `
  -p 6333:6333 -p 6334:6334 `
  -v qdrant_storage:/qdrant/storage:z `
  qdrant/qdrant

# Verify it's running
docker ps | grep qdrant
```

## Daily Usage

### Start Services (Every Time)

```bash
# Terminal 1: Start Ollama
ollama serve

# Terminal 2: Verify Qdrant is running
docker start qdrant

# Terminal 3: Start Worker
cd src/AAR.Worker
dotnet run

# Terminal 4: Start API
cd src/AAR.Api
dotnet run
```

### Health Checks

```bash
# Check all services at once
curl http://localhost:11434/api/tags  # Ollama
curl http://localhost:6333/health     # Qdrant
curl http://localhost:5000/health     # API
```

### Stop Services

```bash
# Stop API/Worker: Ctrl+C in terminals

# Stop Qdrant
docker stop qdrant

# Stop Ollama: Ctrl+C in terminal or:
killall ollama
```

## Test Analysis

### Using PowerShell
```powershell
# Create a test project
$response = Invoke-RestMethod -Uri "http://localhost:5000/api/v1/projects" `
  -Method POST `
  -InFile "test-sample\Repository.cs" `
  -ContentType "multipart/form-data"

$projectId = $response.id

# Start analysis
Invoke-RestMethod -Uri "http://localhost:5000/api/v1/projects/$projectId/analyze" `
  -Method POST

# Check status
Invoke-RestMethod -Uri "http://localhost:5000/api/v1/projects/$projectId"
```

### Using curl
```bash
# Create project from sample
curl -X POST http://localhost:5000/api/v1/projects \
  -F "file=@test-sample/Repository.cs" \
  -F "name=Test Analysis"

# Get project ID from response, then start analysis
curl -X POST http://localhost:5000/api/v1/projects/{project-id}/analyze

# Check status
curl http://localhost:5000/api/v1/projects/{project-id}
```

## Troubleshooting

### Ollama Not Responding
```bash
# Check if running
curl http://localhost:11434/api/tags

# If not, start it
ollama serve

# If still issues, restart with models
ollama stop
ollama serve
```

### Qdrant Not Responding
```bash
# Check status
docker ps -a | grep qdrant

# Restart if needed
docker restart qdrant

# View logs
docker logs qdrant
```

### Model Not Found
```bash
# List available models
ollama list

# Pull missing model
ollama pull qwen2.5-coder:7b
ollama pull bge-large-en-v1.5
```

### High Memory Usage
```bash
# Check memory
docker stats qdrant
Get-Process ollama

# Restart services to free memory
docker restart qdrant
killall ollama && ollama serve
```

## Configuration

### Using Local AI (Default)
File: `src/AAR.Api/appsettings.json` and `src/AAR.Worker/appsettings.json`

```json
{
  "AI": {
    "Provider": "Local"
  }
}
```

### Switch to Azure OpenAI
```json
{
  "AI": {
    "Provider": "Azure",
    "Azure": {
      "Endpoint": "https://your-instance.openai.azure.com",
      "ApiKey": "your-api-key"
    }
  }
}
```

Just change the config and restart - no code changes needed!

## System Requirements

### Minimum
- CPU: 4 cores
- RAM: 8GB
- Storage: 10GB free
- OS: Windows 10/11, Linux, macOS

### Recommended
- CPU: 8 cores
- RAM: 16GB
- Storage: 20GB free SSD
- GPU: Optional (3-5x speedup)

## Performance Expectations

| Operation | Time |
|-----------|------|
| Small file analysis | 3-5s |
| Medium file analysis | 10-15s |
| Embedding generation | 50-500ms |
| Vector search | 10-20ms |
| Small repo (10 files) | 30-60s |
| Medium repo (100 files) | 5-10 min |

## Next Steps

1. ✅ Verify all services are running
2. ✅ Run a test analysis
3. ✅ Check logs: `src/AAR.Api/logs/` and `src/AAR.Worker/logs/`
4. ✅ Monitor resource usage
5. ✅ Review [LOCAL_AI_PRODUCTION_READY.md](docs/LOCAL_AI_PRODUCTION_READY.md) for production deployment

## Documentation

- **Setup Guide**: [docs/LOCAL_AI_SETUP.md](docs/LOCAL_AI_SETUP.md)
- **Architecture**: [docs/LOCAL_AI_SUMMARY.md](docs/LOCAL_AI_SUMMARY.md)
- **Quick Reference**: [docs/LOCAL_AI_QUICK_REFERENCE.md](docs/LOCAL_AI_QUICK_REFERENCE.md)
- **Production Readiness**: [docs/LOCAL_AI_PRODUCTION_READY.md](docs/LOCAL_AI_PRODUCTION_READY.md)
- **Main Documentation**: [README.md](README.md)

## Support

For issues or questions:
1. Check troubleshooting section above
2. Review logs in `src/AAR.Api/logs/` and `src/AAR.Worker/logs/`
3. Consult full documentation in `docs/` directory
4. Check health endpoints for service status
