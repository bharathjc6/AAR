# Autonomous Architecture Reviewer (AAR)

[![CI/CD](https://github.com/your-org/aar/actions/workflows/ci-cd.yml/badge.svg)](https://github.com/your-org/aar/actions/workflows/ci-cd.yml)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://opensource.org/licenses/MIT)

A production-ready .NET 10 application that automatically analyzes code repositories using AI-powered agents to provide comprehensive architecture, security, and code quality reviews.

## 🎯 Features

- **🤖 Local AI Stack**: Production-grade local AI with **zero Azure dependency**
  - LLM: Ollama (`qwen2.5-coder:7b`) - code-specialized 7B model
  - Embeddings: BGE (`bge-large-en-v1.5`) - 1024D SOTA embeddings
  - Vector DB: Qdrant - high-performance similarity search
  - **Cost Savings**: 90-99% vs Azure OpenAI
- **🧠 RAG Pipeline**: Retrieval-Augmented Generation for large repositories
  - Semantic chunking (C#) and sliding window (other languages)
  - Top-K vector retrieval with cosine similarity
  - Context injection for precise analysis
- **Upload Source Code**: Accept ZIP file uploads or clone directly from GitHub URLs
- **Multi-Agent Analysis**: Four specialized AI agents analyze different aspects of your code:
  - 🏗️ **Structure Agent**: Folder organization, naming conventions, project structure
  - 🔍 **Code Quality Agent**: Complexity, code smells, best practices, performance
  - 🔒 **Security Agent**: OWASP Top 10, vulnerability detection, secrets scanning
  - 📐 **Architecture Advisor**: Design patterns, scalability, technology recommendations
- **Consolidated Reports**: Generate comprehensive JSON and PDF reports
- **RESTful API**: Full API with versioning, authentication, and OpenAPI documentation
- **Async Processing**: Background worker service for non-blocking analysis
- **Provider-Agnostic**: Switch between Local AI and Azure OpenAI with 1 config change

## 🏛️ Architecture

The project follows **Clean Architecture** principles:

```
┌─────────────────────────────────────────────────────────────┐
│                        API Layer                             │
│              (Controllers, Middleware, DTOs)                 │
├─────────────────────────────────────────────────────────────┤
│                    Application Layer                         │
│           (Services, Interfaces, Validators)                 │
├─────────────────────────────────────────────────────────────┤
│                      Domain Layer                            │
│         (Entities, Value Objects, Repository Interfaces)     │
├─────────────────────────────────────────────────────────────┤
│                   Infrastructure Layer                       │
│     (EF Core, Azure SDK, External Services, Repositories)    │
└─────────────────────────────────────────────────────────────┘
```

## 🚀 Quick Start

### Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- [Ollama](https://ollama.ai/download) (for Local AI)
- [Docker](https://www.docker.com/get-started) (for Qdrant vector database)

### Local AI Setup (Recommended)

**Step 1: Install Ollama**
```bash
# macOS
brew install ollama

# Linux
curl -fsSL https://ollama.ai/install.sh | sh

# Windows: Download from https://ollama.ai/download
```

**Step 2: Pull AI Models**
```bash
ollama pull qwen2.5-coder:7b      # LLM (4.7 GB)
ollama pull bge-large-en-v1.5     # Embeddings (1.3 GB)
```

**Step 3: Start Qdrant (Vector Database)**
```bash
docker run -d --name qdrant \
  -p 6333:6333 -p 6334:6334 \
  -v qdrant_storage:/qdrant/storage \
  qdrant/qdrant
```

**Step 4: Clone and Configure**
```bash
git clone https://github.com/your-org/aar.git
cd aar

# Configuration is already set for Local AI in appsettings.json
# Just verify AI.Provider = "Local"
```

**Step 5: Run Services**
```bash
# Terminal 1: Start Ollama
ollama serve

# Terminal 2: Run API
cd src/AAR.Api
dotnet run

# Terminal 3: Run Worker
cd src/AAR.Worker
dotnet run
```

**Step 6: Access the Application**
- Swagger UI: http://localhost:5000/swagger
- Health Check: http://localhost:5000/health

> 📚 **Detailed Setup**: See [docs/LOCAL_AI_SETUP.md](docs/LOCAL_AI_SETUP.md) for complete instructions

### Alternative: Azure OpenAI Setup

To use Azure OpenAI instead of local AI, update `appsettings.json`:
```json
{
  "AI": {
    "Provider": "Azure",
    "Azure": {
      "Endpoint": "https://YOUR-RESOURCE.openai.azure.com/",
      "ApiKey": "YOUR_API_KEY"
    }
  }
}
```

### Local Development (Legacy)

1. **Clone the repository**
   ```bash
   git clone https://github.com/your-org/aar.git
   cd aar
   ```

2. **Restore and build**
   ```bash
   dotnet restore
   dotnet build
   ```

3. **Run the API**
   ```bash
   cd src/AAR.Api
   dotnet run
   ```

4. **Run the Worker** (in a separate terminal)
   ```bash
   cd src/AAR.Worker
   dotnet run
   ```

5. **Access the API**
   - Swagger UI: http://localhost:5000/swagger
   - Health Check: http://localhost:5000/health

### Docker Deployment

```bash
# Build and run with Docker Compose
docker-compose up --build

# Access the API at http://localhost:5000
```

## 📖 API Usage

### Create a Project

```bash
curl -X POST http://localhost:5000/api/v1/projects \
  -H "Content-Type: application/json" \
  -H "X-API-Key: your-api-key" \
  -d '{"name": "My Project", "description": "Project description"}'
```

### Upload Source Code

```bash
curl -X POST http://localhost:5000/api/v1/projects/{projectId}/upload \
  -H "X-API-Key: your-api-key" \
  -F "file=@source-code.zip"
```

### Clone from GitHub

```bash
curl -X POST http://localhost:5000/api/v1/projects/{projectId}/clone \
  -H "Content-Type: application/json" \
  -H "X-API-Key: your-api-key" \
  -d '{"url": "https://github.com/owner/repo"}'
```

### Get Report

```bash
# JSON Report
curl http://localhost:5000/api/v1/reports/project/{projectId} \
  -H "X-API-Key: your-api-key"

# PDF Report
curl http://localhost:5000/api/v1/reports/{reportId}/pdf \
  -H "X-API-Key: your-api-key" \
  -o report.pdf
```

## ⚙️ Configuration

### AI Provider Configuration

AAR supports two AI providers:

#### 🤖 Local AI (Default - Recommended)
Zero cloud costs, full privacy, air-gap compatible

```json
{
  "AI": {
    "Provider": "Local",
    "Local": {
      "OllamaUrl": "http://localhost:11434",
      "LLMModel": "qwen2.5-coder:7b",
      "EmbeddingModel": "bge-large-en-v1.5",
      "TimeoutSeconds": 120
    },
    "VectorDb": {
      "Type": "Qdrant",
      "Url": "http://localhost:6333",
      "Dimension": 1024
    },
    "Rag": {
      "SmallFileThresholdBytes": 10240,
      "ChunkSizeTokens": 512,
      "TopK": 10
    }
  }
}
```

**Requirements:**
- Ollama with models: `qwen2.5-coder:7b` (4.7 GB), `bge-large-en-v1.5` (1.3 GB)
- Qdrant running on port 6333
- 8-16 GB RAM recommended

**Cost:** ~$5-10/month (electricity)

#### ☁️ Azure OpenAI (Optional)
Enterprise cloud option with Azure integration

```json
{
  "AI": {
    "Provider": "Azure",
    "Azure": {
      "Endpoint": "https://YOUR-RESOURCE.openai.azure.com/",
      "ApiKey": "YOUR_API_KEY",
      "LLMDeployment": "gpt-4",
      "EmbeddingDeployment": "text-embedding-ada-002"
    },
    "VectorDb": {
      "Type": "Qdrant",
      "Dimension": 1536
    }
  }
}
```

**Requirements:**
- Azure subscription
- Azure OpenAI resource with gpt-4 and embedding deployments

**Cost:** ~$50-500/month (per 10 analyses)

> **📝 Note:** Switch between providers with **zero code changes** - just update the configuration!

### Other Environment Variables

| Variable | Description | Default |
|----------|-------------|---------|
| `AI__Provider` | AI provider (`Local` or `Azure`) | `Local` |
| `AI__Local__OllamaUrl` | Ollama API URL | `http://localhost:11434` |
| `AI__VectorDb__Url` | Qdrant URL | `http://localhost:6333` |
| `ConnectionStrings__DefaultConnection` | Database connection string | `Data Source=aar.db` |
| `UseSqlServer` | Use SQL Server instead of SQLite | `false` |
| `BlobStorage__Provider` | Storage provider (`FileSystem` or `Azure`) | `FileSystem` |
| `QueueService__Provider` | Queue provider (`InMemory` or `Azure`) | `InMemory` |

## 🧪 Testing

```bash
# Run all tests
dotnet test

# Run with coverage
dotnet test --collect:"XPlat Code Coverage"

# Run specific test project
dotnet test tests/AAR.Tests/AAR.Tests.csproj
```

## 📁 Project Structure

```
AAR/
├── src/
│   ├── AAR.Shared/          # Common utilities, Result pattern
│   ├── AAR.Domain/          # Entities, Value Objects, Interfaces
│   ├── AAR.Application/     # Business logic, Services, DTOs
│   ├── AAR.Infrastructure/  # Data access, External services
│   ├── AAR.Api/             # REST API, Controllers, Middleware
│   └── AAR.Worker/          # Background processing, Agents
├── tests/
│   └── AAR.Tests/           # Unit and integration tests
├── prompts/                 # AI agent prompt templates
├── docs/                    # Documentation
├── .github/workflows/       # CI/CD pipelines
├── docker-compose.yml       # Development deployment
└── docker-compose.prod.yml  # Production deployment
```

## 🔒 Security

- API Key authentication for all endpoints
- No secrets stored in code (use environment variables)
- Input validation with FluentValidation
- SQL injection prevention via parameterized queries
- XSS protection in responses
- Rate limiting (configurable)

## 📊 Report Output

The analysis generates reports including:

- **Overall Score** (0-100)
- **Category Scores**: Structure, Code Quality, Security, Architecture
- **Findings by Severity**: Critical, High, Medium, Low, Info
- **Detailed Findings**: With file paths, line numbers, code snippets, and suggestions

Example JSON response:
```json
{
  "id": "guid",
  "projectId": "guid",
  "overallScore": 85,
  "structureScore": 90,
  "codeQualityScore": 82,
  "securityScore": 88,
  "architectureScore": 80,
  "summary": "Analysis complete with 15 findings...",
  "criticalCount": 0,
  "highCount": 2,
  "mediumCount": 5,
  "lowCount": 6,
  "infoCount": 2,
  "generatedAt": "2024-01-15T10:30:00Z"
}
```

## 🛠️ Development

### Code Style

- Follow C# coding conventions
- Use nullable reference types
- Prefer records for DTOs
- Use async/await consistently

### Adding a New Agent

1. Create a new agent class in `AAR.Worker/Agents/`
2. Implement `IAnalysisAgent` interface
3. Register in `Program.cs`
4. Create a prompt template in `prompts/`

## 📄 License

This project is licensed under the MIT License - see the [LICENSE](LICENSE) file for details.

## 🤝 Contributing

1. Fork the repository
2. Create a feature branch (`git checkout -b feature/amazing-feature`)
3. Commit changes (`git commit -m 'Add amazing feature'`)
4. Push to branch (`git push origin feature/amazing-feature`)
5. Open a Pull Request

## 📞 Support

- 📧 Email: support@example.com
- 🐛 Issues: [GitHub Issues](https://github.com/your-org/aar/issues)
- 📖 Documentation: See [docs/](./docs/) directory

## 📚 Documentation

### Getting Started
- **[LOCAL_AI_SETUP.md](docs/LOCAL_AI_SETUP.md)** - Complete local AI setup guide
- **[LOCAL_AI_QUICK_REFERENCE.md](docs/LOCAL_AI_QUICK_REFERENCE.md)** - Quick reference card
- **[LOCAL_AI_SUMMARY.md](docs/LOCAL_AI_SUMMARY.md)** - Implementation details

### Architecture & Design
- **[ARCHITECTURE.md](docs/ARCHITECTURE.md)** - System architecture overview
- **[RAG_PIPELINE.md](docs/RAG_PIPELINE.md)** - RAG pipeline implementation
- **[THREAT_MODEL.md](docs/THREAT_MODEL.md)** - Security threat model

### Migrations & Updates
- **[MIGRATION_KEYVAULT.md](docs/MIGRATION_KEYVAULT.md)** - Key Vault integration
- **[MIGRATION_RAG.md](docs/MIGRATION_RAG.md)** - RAG pipeline migration
- **[MIGRATION_SCALE.md](docs/MIGRATION_SCALE.md)** - Scaling improvements
- **[MIGRATION_FIX_STREAMING_STUCK.md](docs/MIGRATION_FIX_STREAMING_STUCK.md)** - Batch processing fixes

### Testing
- **[TEST_PLAN.md](docs/TEST_PLAN.md)** - Comprehensive test plan

---

**🚀 Ready to analyze your code with enterprise-grade local AI!**
