# Local AI Setup Guide

## Overview

AAR now supports a **production-grade local AI stack** as an alternative to Azure OpenAI, eliminating cloud costs while maintaining enterprise-level functionality.

### Tech Stack

- **LLM**: Ollama with `qwen2.5-coder:7b` (7B parameter code-specialized model)
- **Embeddings**: BGE (`bge-large-en-v1.5`, 1024 dimensions)
- **Vector Database**: Qdrant (self-hosted or cloud)
- **RAG Pipeline**: Fully integrated with semantic chunking

### Architecture Benefits

✅ **Provider-Agnostic**: Switch between Local and Azure with 1 config change  
✅ **Clean Architecture**: No AI SDK usage in Application layer  
✅ **Enterprise-Ready**: Retry policies, timeouts, structured logging  
✅ **RAG-Enabled**: Automatic chunking and vector retrieval for large repos  
✅ **Future-Proof**: Azure OpenAI provider ready but disabled  

---

## Prerequisites

### 1. Install Ollama

**Windows/Mac/Linux:**
```bash
# Download from https://ollama.ai/download
# Or use package manager:

# macOS
brew install ollama

# Linux
curl -fsSL https://ollama.ai/install.sh | sh

# Windows: Download installer from website
```

**Verify Installation:**
```bash
ollama --version
```

### 2. Pull Required Models

**LLM Model (qwen2.5-coder:7b):**
```bash
ollama pull qwen2.5-coder:7b
```
> **Model Size**: ~4.7 GB  
> **Recommended RAM**: 8 GB minimum, 16 GB recommended

**Embedding Model (bge-large-en-v1.5):**
```bash
ollama pull bge-large-en-v1.5
```
> **Model Size**: ~1.3 GB  
> **Dimensions**: 1024

**Verify Models:**
```bash
ollama list
```

Expected output:
```
NAME                     ID              SIZE     MODIFIED
qwen2.5-coder:7b         abc123def456    4.7 GB   2 hours ago
bge-large-en-v1.5        def789ghi012    1.3 GB   2 hours ago
```

### 3. Install Qdrant

**Option A: Docker (Recommended)**
```bash
docker run -d \
  --name qdrant \
  -p 6333:6333 \
  -p 6334:6334 \
  -v qdrant_storage:/qdrant/storage \
  qdrant/qdrant
```

**Option B: Docker Compose**

Create `docker-compose.qdrant.yml`:
```yaml
version: '3.8'
services:
  qdrant:
    image: qdrant/qdrant:latest
    ports:
      - "6333:6333"
      - "6334:6334"
    volumes:
      - qdrant_storage:/qdrant/storage
    environment:
      - QDRANT__SERVICE__GRPC_PORT=6334

volumes:
  qdrant_storage:
```

Start:
```bash
docker-compose -f docker-compose.qdrant.yml up -d
```

**Option C: Qdrant Cloud (Production)**

1. Sign up at https://cloud.qdrant.io
2. Create a cluster
3. Get API URL and API Key
4. Update `appsettings.json`:
```json
"AI": {
  "VectorDb": {
    "Type": "Qdrant",
    "Url": "https://YOUR-CLUSTER.cloud.qdrant.io",
    "ApiKey": "YOUR_API_KEY"
  }
}
```

**Verify Qdrant:**
```bash
curl http://localhost:6333/healthz
```

Expected: `{"status":"ok"}`

---

## Configuration

### 1. Update `appsettings.json`

**API (`src/AAR.Api/appsettings.json`):**
```json
{
  "AI": {
    "Provider": "Local",
    "Local": {
      "OllamaUrl": "http://localhost:11434",
      "LLMModel": "qwen2.5-coder:7b",
      "EmbeddingModel": "bge-large-en-v1.5",
      "TimeoutSeconds": 120,
      "MaxRetries": 3,
      "Temperature": 0.3,
      "MaxTokens": 4096
    },
    "VectorDb": {
      "Type": "Qdrant",
      "Url": "http://localhost:6333",
      "ApiKey": "",
      "CollectionPrefix": "aar",
      "Dimension": 1024
    },
    "Rag": {
      "SmallFileThresholdBytes": 10240,
      "ChunkSizeTokens": 512,
      "ChunkOverlapPercent": 20,
      "TopK": 10,
      "MinSimilarityScore": 0.7,
      "MaxContextTokens": 8000
    }
  }
}
```

**Worker (`src/AAR.Worker/appsettings.json`):**
Same configuration as API.

### 2. Environment Variables (Optional)

For production, you can override config via environment variables:

```bash
export AI__Provider=Local
export AI__Local__OllamaUrl=http://localhost:11434
export AI__VectorDb__Url=http://localhost:6333
```

---

## Running the System

### 1. Start Services

**Terminal 1 - Ollama:**
```bash
ollama serve
```
> Ollama runs on port `11434` by default

**Terminal 2 - Qdrant:**
```bash
docker start qdrant
# Or if using Docker Compose:
docker-compose -f docker-compose.qdrant.yml up
```

**Terminal 3 - AAR API:**
```bash
cd src/AAR.Api
dotnet run
```

**Terminal 4 - AAR Worker:**
```bash
cd src/AAR.Worker
dotnet run
```

### 2. Verify Setup

**Check Ollama:**
```bash
curl http://localhost:11434/api/tags
```

**Check Qdrant:**
```bash
curl http://localhost:6333/collections
```

**Check AAR API:**
```bash
curl http://localhost:5000/api/v1/health
```

---

## RAG Pipeline Workflow

### How It Works

1. **File Classification**:
   - Files < 10 KB → Sent directly to LLM
   - Files ≥ 10 KB → Chunked and embedded

2. **Chunking**:
   - Semantic chunking for C# (method/class level)
   - Sliding window for other languages
   - Chunk size: 512 tokens, 20% overlap

3. **Embedding**:
   - BGE embeddings (1024D)
   - Normalized vectors
   - Batch processing (16 per batch)

4. **Vector Storage**:
   - Stored in Qdrant with metadata:
     - `project_id`
     - `file_path`
     - `start_line`, `end_line`
     - `language`
     - `semantic_type` (class/method)
     - `semantic_name`

5. **Retrieval**:
   - Query embedding generated
   - Top-K similar chunks retrieved (default: 10)
   - Cosine similarity threshold: 0.7
   - Chunks injected into LLM prompt

6. **Analysis**:
   - LLM analyzes with retrieved context
   - Findings returned as JSON

---

## Switching to Azure OpenAI

To switch to Azure OpenAI (e.g., for production with budget):

### 1. Update `appsettings.json`:
```json
{
  "AI": {
    "Provider": "Azure",
    "Azure": {
      "Endpoint": "https://YOUR-RESOURCE.openai.azure.com/",
      "ApiKey": "YOUR_API_KEY",
      "LLMDeployment": "gpt-4",
      "EmbeddingDeployment": "text-embedding-ada-002",
      "TimeoutSeconds": 120
    },
    "VectorDb": {
      "Dimension": 1536
    }
  }
}
```

### 2. Restart Services

**That's it!** No code changes required.

---

## Performance Tuning

### LLM Settings

**For Faster Responses:**
```json
"Local": {
  "Temperature": 0.1,
  "MaxTokens": 2048
}
```

**For Better Quality:**
```json
"Local": {
  "Temperature": 0.5,
  "MaxTokens": 8192
}
```

### Embedding Batch Size

Adjust based on system resources:
```json
"Concurrency": {
  "EmbeddingBatchSize": 32,
  "EmbeddingConcurrency": 8
}
```

### Qdrant Performance

- Use SSD storage for production
- Consider Qdrant Cloud for horizontal scaling
- Enable HNSW indexing for large datasets (automatic)

---

## Troubleshooting

### Issue: "Failed to connect to Ollama"

**Solution:**
```bash
# Check if Ollama is running
curl http://localhost:11434/api/tags

# Start Ollama if not running
ollama serve
```

### Issue: "Model not found: qwen2.5-coder:7b"

**Solution:**
```bash
ollama pull qwen2.5-coder:7b
ollama list  # Verify model is downloaded
```

### Issue: "Qdrant connection refused"

**Solution:**
```bash
# Check if Qdrant is running
docker ps | grep qdrant

# Start Qdrant if stopped
docker start qdrant
# Or
docker-compose -f docker-compose.qdrant.yml up -d
```

### Issue: "Out of memory when processing large repos"

**Solution:**
Adjust memory limits in `appsettings.json`:
```json
"MemoryManagement": {
  "MaxWorkerMemoryMB": 8192,
  "MemoryWarningThresholdPercent": 80
}
```

### Issue: "Embeddings taking too long"

**Solution:**
Reduce batch size or increase concurrency:
```json
"Concurrency": {
  "EmbeddingBatchSize": 8,
  "EmbeddingConcurrency": 4
}
```

---

## Model Alternatives

### LLM Models

| Model | Size | Quality | Speed | Use Case |
|-------|------|---------|-------|----------|
| `qwen2.5-coder:7b` | 4.7 GB | ⭐⭐⭐⭐ | ⭐⭐⭐ | **Recommended** |
| `codellama:7b` | 3.8 GB | ⭐⭐⭐ | ⭐⭐⭐⭐ | Faster, lower quality |
| `deepseek-coder:6.7b` | 3.7 GB | ⭐⭐⭐⭐ | ⭐⭐⭐ | Alternative |
| `qwen2.5-coder:14b` | 8.9 GB | ⭐⭐⭐⭐⭐ | ⭐⭐ | Best quality (needs 32GB RAM) |

**Change Model:**
```json
"AI": {
  "Local": {
    "LLMModel": "codellama:7b"
  }
}
```

Then:
```bash
ollama pull codellama:7b
```

### Embedding Models

| Model | Size | Dimensions | Quality |
|-------|------|------------|---------|
| `bge-large-en-v1.5` | 1.3 GB | 1024 | ⭐⭐⭐⭐⭐ **Recommended** |
| `bge-small-en-v1.5` | 133 MB | 384 | ⭐⭐⭐ (Faster) |
| `nomic-embed-text` | 274 MB | 768 | ⭐⭐⭐⭐ |

**Note**: If you change embedding model, update `Dimension` in config!

---

## Monitoring

### Logs to Watch

**Successful Initialization:**
```
[Information] OllamaLLMProvider initialized with model: qwen2.5-coder:7b
[Information] OllamaEmbeddingProvider initialized with model: bge-large-en-v1.5 (1024D)
[Information] QdrantVectorStore initialized (collection: aar_vectors, dimension: 1024)
```

**RAG Processing:**
```
[Information] Chunking 45 files for project {ProjectId}
[Information] Generated embeddings for 120 chunks
[Information] Indexed 120 vectors in Qdrant
[Information] Retrieved 10 relevant chunks for analysis
```

**LLM Requests:**
```
[Information] LLM request completed in 3542ms
[Information] Generated 1234 tokens (prompt: 567, completion: 667)
```

---

## Cost Comparison

### Local AI (This Setup)

- **Hardware**: One-time cost (8-32 GB RAM, GPU optional)
- **Electricity**: ~$5-10/month (always-on server)
- **Total Monthly**: **~$5-10**

### Azure OpenAI

- **gpt-4**: $30 per 1M input tokens, $60 per 1M output tokens
- **text-embedding-ada-002**: $0.10 per 1M tokens
- **Typical Large Repo Analysis**: $5-50 per run
- **Total Monthly (10 analyses)**: **$50-500**

**Savings**: **90-99% cost reduction** with local setup

---

## Security Considerations

✅ **All data stays local** - No cloud transmission  
✅ **No API keys to manage** (for Local mode)  
✅ **Air-gap compatible** - Works offline  
✅ **GDPR compliant** - No third-party data processing  

---

## Production Deployment

### Recommended Stack

```
┌─────────────────────────────────────┐
│  Load Balancer (nginx/traefik)     │
└─────────────────┬───────────────────┘
                  │
     ┌────────────┴────────────┐
     │                         │
┌────▼──────┐         ┌───────▼────┐
│  AAR API  │         │ AAR Worker │
│  (3 pods) │         │  (5 pods)  │
└───────────┘         └───────┬────┘
                              │
              ┌───────────────┴───────────────┐
              │                               │
     ┌────────▼─────────┐          ┌─────────▼────────┐
     │  Ollama Cluster  │          │ Qdrant Cluster   │
     │  (GPU-enabled)   │          │ (HA setup)       │
     └──────────────────┘          └──────────────────┘
```

### Docker Compose (Production)

Create `docker-compose.prod.yml`:
```yaml
version: '3.8'

services:
  ollama:
    image: ollama/ollama:latest
    ports:
      - "11434:11434"
    volumes:
      - ollama_models:/root/.ollama
    deploy:
      resources:
        reservations:
          devices:
            - driver: nvidia
              count: 1
              capabilities: [gpu]

  qdrant:
    image: qdrant/qdrant:latest
    ports:
      - "6333:6333"
    volumes:
      - qdrant_storage:/qdrant/storage
    environment:
      - QDRANT__SERVICE__HTTP_PORT=6333

  aar-api:
    build:
      context: .
      dockerfile: Dockerfile.api
    ports:
      - "5000:80"
    environment:
      - AI__Provider=Local
      - AI__Local__OllamaUrl=http://ollama:11434
      - AI__VectorDb__Url=http://qdrant:6333
    depends_on:
      - ollama
      - qdrant

  aar-worker:
    build:
      context: .
      dockerfile: Dockerfile.worker
    environment:
      - AI__Provider=Local
      - AI__Local__OllamaUrl=http://ollama:11434
      - AI__VectorDb__Url=http://qdrant:6333
    depends_on:
      - ollama
      - qdrant
    deploy:
      replicas: 3

volumes:
  ollama_models:
  qdrant_storage:
```

---

## Next Steps

1. ✅ **Setup Complete** - Verify all services are running
2. 📁 **Test with Sample Repo** - Analyze a small project first
3. 🔧 **Tune Performance** - Adjust batch sizes and concurrency
4. 📊 **Monitor Metrics** - Watch logs for performance bottlenecks
5. 🚀 **Scale Up** - Add more workers or upgrade to GPU

---

## Support

For issues or questions:
- Check logs in `logs/` directory
- Review [ARCHITECTURE.md](ARCHITECTURE.md) for system design
- See [RAG_PIPELINE.md](RAG_PIPELINE.md) for RAG details

---

**🎉 Your local AI stack is now production-ready!**
