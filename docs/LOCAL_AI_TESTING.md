# Local AI Services Setup Guide

This guide explains how to set up and run AAR with local Ollama and Qdrant services instead of mocks for development and testing.

## Prerequisites

- **Docker Desktop** installed and running
- **PowerShell** (Windows) or **Bash** (Linux/Mac)
- At least **10GB free disk space** for AI models
- **8GB RAM minimum**, 16GB recommended

## Quick Start

### 1. Start Local Services

```powershell
# Start Ollama and Qdrant
.\scripts\start-local-services.ps1
```

This script will:
- ✓ Verify Docker is running
- ✓ Start Ollama and Qdrant containers
- ✓ Wait for services to be healthy
- ✓ Display service URLs

### 2. Pull Required AI Models

```powershell
# Download Ollama models (one-time setup, ~5GB download)
.\scripts\setup-ollama-models.ps1
```

This will pull:
- **qwen2.5-coder:7b** (~4.7GB) - LLM for code analysis
- **bge-large-en-v1.5** (~670MB) - Embedding model (1024D)

⏱️ **First-time setup takes 10-30 minutes** depending on your internet speed.

### 3. Run Tests

```powershell
# Run all integration tests
dotnet test

# Run specific test project
dotnet test tests/AAR.Tests/AAR.Tests.csproj

# Run with verbose output
dotnet test --logger "console;verbosity=detailed"
```

### 4. Start the Full Application

```powershell
# Start all services (API, Worker, Frontend)
docker-compose up -d

# View logs
docker-compose logs -f

# Stop services
docker-compose down
```

## Service URLs

When running locally:

| Service | URL | Description |
|---------|-----|-------------|
| Ollama API | http://localhost:11434 | LLM and embedding generation |
| Qdrant Dashboard | http://localhost:6333/dashboard | Vector database UI |
| AAR API | http://localhost:5000 | Backend API |
| AAR Frontend | http://localhost:3000 | React frontend |

## Configuration

### Development Configuration

The following files have been updated to use local services:

- `src/AAR.Api/appsettings.Development.json`
- `src/AAR.Worker/appsettings.Development.json`
- `docker-compose.yml`
- `docker-compose.e2e.yml`

Key settings:
```json
{
  "AI": {
    "Provider": "Local",
    "Local": {
      "OllamaUrl": "http://localhost:11434",
      "LLMModel": "qwen2.5-coder:7b",
      "EmbeddingModel": "bge-large-en-v1.5"
    },
    "VectorDb": {
      "Type": "Qdrant",
      "Url": "http://localhost:6333",
      "CollectionPrefix": "aar_dev",
      "Dimension": 1024
    }
  },
  "Embedding": {
    "UseMock": false
  }
}
```

### Test Configuration

Tests now use real Ollama and Qdrant services. The `AarWebApplicationFactory` has been updated to:
- ✅ Use real Ollama LLM provider
- ✅ Use real Qdrant vector store
- ✅ Use real embedding generation
- ❌ No more mock services

## Running E2E Tests

### With Docker Compose

```powershell
# Start E2E environment (includes SQL Server, Ollama, Qdrant, API, Frontend)
docker-compose -f docker-compose.e2e.yml up -d

# Wait for services to be ready (~2 minutes)
docker-compose -f docker-compose.e2e.yml ps

# Run Playwright tests
cd aar-frontend
npm run test:e2e

# Cleanup
docker-compose -f docker-compose.e2e.yml down -v
```

### E2E Test Environment

The E2E stack includes:
- SQL Server 2022
- Ollama with models pre-loaded
- Qdrant vector database
- AAR API (with integrated worker)
- AAR Frontend (nginx)
- Playwright test runner

## Troubleshooting

### Ollama Not Starting

```powershell
# Check Ollama logs
docker logs aar-ollama

# Restart Ollama
docker-compose restart ollama

# Verify Ollama is accessible
curl http://localhost:11434/api/tags
```

### Qdrant Not Starting

```powershell
# Check Qdrant logs
docker logs aar-qdrant

# Restart Qdrant
docker-compose restart qdrant

# Verify Qdrant is accessible
curl http://localhost:6333/health
```

### Models Not Found

If tests fail with "model not found":

```powershell
# List available models
ollama list

# Pull missing model manually
ollama pull qwen2.5-coder:7b
ollama pull bge-large-en-v1.5

# Or use the setup script
.\scripts\setup-ollama-models.ps1
```

### Tests Timing Out

If tests timeout waiting for AI responses:

1. Check if Ollama is running: `docker ps | grep ollama`
2. Verify models are loaded: `ollama list`
3. Check Ollama resource usage: `docker stats aar-ollama`
4. Increase test timeouts in `tests/AAR.Tests/xunit.runner.json`

### Out of Memory

Ollama + models require significant RAM:
- Minimum: 8GB system RAM
- Recommended: 16GB system RAM
- Per model: ~4-8GB RAM when loaded

To reduce memory usage:
- Use smaller models (e.g., `qwen2.5-coder:1.5b` instead of `7b`)
- Stop other Docker containers
- Close memory-intensive applications

## Performance Tips

### GPU Acceleration (Optional)

If you have an NVIDIA GPU:

1. Install [NVIDIA Container Toolkit](https://docs.nvidia.com/datacenter/cloud-native/container-toolkit/install-guide.html)
2. Uncomment GPU configuration in `docker-compose.yml`:
   ```yaml
   deploy:
     resources:
       reservations:
         devices:
           - driver: nvidia
             count: all
             capabilities: [gpu]
   ```
3. Restart Ollama: `docker-compose up -d ollama`

GPU acceleration can improve inference speed by 5-10x.

### Model Caching

Models are cached in Docker volumes:
- `ollama-data` - Model weights and configuration
- `qdrant-data` - Vector database storage

These persist across container restarts.

## Switching Back to Mocks

To temporarily use mocks instead of local services:

1. Set environment variable:
   ```powershell
   $env:Embedding__UseMock = "true"
   $env:AI__Provider = "Mock"
   ```

2. Or update `appsettings.Development.json`:
   ```json
   {
     "Embedding": {
       "UseMock": true
     }
   }
   ```

## Production Deployment

For production, use Azure OpenAI instead of local Ollama:

```json
{
  "AI": {
    "Provider": "Azure",
    "Azure": {
      "Endpoint": "https://your-resource.openai.azure.com",
      "ApiKey": "<from-keyvault>",
      "LLMDeployment": "gpt-4",
      "EmbeddingDeployment": "text-embedding-ada-002"
    },
    "VectorDb": {
      "Type": "Qdrant",
      "Url": "https://your-qdrant-cloud.io",
      "ApiKey": "<from-keyvault>"
    }
  }
}
```

## Additional Resources

- [Ollama Documentation](https://ollama.ai/docs)
- [Qdrant Documentation](https://qdrant.tech/documentation/)
- [BGE Embedding Models](https://huggingface.co/BAAI/bge-large-en-v1.5)
- [Qwen2.5-Coder Models](https://huggingface.co/Qwen/Qwen2.5-Coder-7B)

## Summary

✅ **What Changed:**
- Removed all mock implementations from tests
- Configured local Ollama for LLM and embeddings
- Configured local Qdrant for vector storage
- Updated Docker Compose for E2E testing
- Created setup scripts for easy configuration

✅ **Benefits:**
- More realistic testing environment
- Test actual AI integration behavior
- Catch issues before production
- Faster feedback loops vs cloud APIs
- No API costs for development

✅ **Next Steps:**
1. Run `.\scripts\start-local-services.ps1`
2. Run `.\scripts\setup-ollama-models.ps1`
3. Run `dotnet test`
4. Start developing with confidence! 🚀
