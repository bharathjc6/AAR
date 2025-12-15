# Local AI Quick Reference

## 🚀 Quick Start Commands

### Install Ollama
```bash
# macOS
brew install ollama

# Linux
curl -fsSL https://ollama.ai/install.sh | sh

# Windows: Download from https://ollama.ai/download
```

### Pull Models
```bash
ollama pull qwen2.5-coder:7b      # LLM (4.7 GB)
ollama pull bge-large-en-v1.5     # Embeddings (1.3 GB)
```

### Start Qdrant
```bash
docker run -d --name qdrant \
  -p 6333:6333 -p 6334:6334 \
  -v qdrant_storage:/qdrant/storage \
  qdrant/qdrant
```

### Start Services
```bash
# Terminal 1
ollama serve

# Terminal 2
cd src/AAR.Api && dotnet run

# Terminal 3
cd src/AAR.Worker && dotnet run
```

---

## ⚙️ Configuration

### Switch to Local AI
```json
{
  "AI": {
    "Provider": "Local"
  }
}
```

### Switch to Azure
```json
{
  "AI": {
    "Provider": "Azure",
    "Azure": {
      "Endpoint": "https://YOUR-RESOURCE.openai.azure.com/",
      "ApiKey": "YOUR_KEY"
    }
  }
}
```

---

## 🔍 Health Checks

```bash
# Ollama
curl http://localhost:11434/api/tags

# Qdrant
curl http://localhost:6333/healthz

# AAR API
curl http://localhost:5000/api/v1/health
```

---

## 📊 Default Settings

| Setting | Value | Notes |
|---------|-------|-------|
| LLM Model | `qwen2.5-coder:7b` | 4.7 GB, code-specialized |
| Embedding Model | `bge-large-en-v1.5` | 1.3 GB, 1024 dimensions |
| Vector DB | Qdrant | `http://localhost:6333` |
| Chunk Size | 512 tokens | With 20% overlap |
| Top-K Retrieval | 10 chunks | Min similarity: 0.7 |
| LLM Temperature | 0.3 | Lower = more deterministic |
| Max Tokens | 4096 | LLM output limit |

---

## 🛠️ Troubleshooting

### Ollama not responding
```bash
# Check if running
ps aux | grep ollama

# Restart
killall ollama
ollama serve
```

### Model not found
```bash
ollama list  # Check downloaded models
ollama pull qwen2.5-coder:7b  # Re-download if missing
```

### Qdrant connection refused
```bash
docker ps | grep qdrant  # Check if running
docker start qdrant      # Restart if stopped
docker logs qdrant       # Check logs
```

### Out of memory
```json
{
  "MemoryManagement": {
    "MaxWorkerMemoryMB": 8192
  }
}
```

---

## 🔧 Performance Tuning

### Faster (lower quality)
```json
{
  "AI": {
    "Local": {
      "LLMModel": "codellama:7b",
      "Temperature": 0.1,
      "MaxTokens": 2048
    }
  }
}
```

### Better (slower)
```json
{
  "AI": {
    "Local": {
      "LLMModel": "qwen2.5-coder:14b",
      "Temperature": 0.5,
      "MaxTokens": 8192
    }
  }
}
```

---

## 📁 Key Files

| File | Purpose |
|------|---------|
| `src/AAR.Api/appsettings.json` | API configuration |
| `src/AAR.Worker/appsettings.json` | Worker configuration |
| `docs/LOCAL_AI_SETUP.md` | Full setup guide |
| `docs/LOCAL_AI_SUMMARY.md` | Implementation details |

---

## 🔄 RAG Pipeline Flow

```
File → Chunker → Embeddings → Qdrant
                                  ↓
Query → Embedding → Search → Top-K Chunks
                                  ↓
                         LLM Analysis
```

---

## 💰 Cost Savings

| Setup | Monthly Cost |
|-------|--------------|
| Local AI | $5-10 (electricity) |
| Azure OpenAI | $50-500 (per 10 analyses) |
| **Savings** | **90-99%** |

---

## 📚 Documentation

- [LOCAL_AI_SETUP.md](LOCAL_AI_SETUP.md) - Setup guide
- [LOCAL_AI_SUMMARY.md](LOCAL_AI_SUMMARY.md) - Implementation summary
- [ARCHITECTURE.md](ARCHITECTURE.md) - System architecture
- [RAG_PIPELINE.md](RAG_PIPELINE.md) - RAG details

---

## 🎯 Success Checklist

- [ ] Ollama installed and running
- [ ] Models downloaded (`qwen2.5-coder:7b`, `bge-large-en-v1.5`)
- [ ] Qdrant running (Docker)
- [ ] `appsettings.json` set to `"Provider": "Local"`
- [ ] API and Worker running
- [ ] Health checks passing
- [ ] Test analysis completed

---

**Ready to analyze your first repo with local AI! 🚀**
