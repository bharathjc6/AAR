# Autonomous Architecture Reviewer (AAR)

[![CI/CD](https://github.com/your-org/aar/actions/workflows/ci-cd.yml/badge.svg)](https://github.com/your-org/aar/actions/workflows/ci-cd.yml)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://opensource.org/licenses/MIT)

A production-ready .NET 10 application that automatically analyzes code repositories using AI-powered agents to provide comprehensive architecture, security, and code quality reviews.

## 🎯 Features

- **Upload Source Code**: Accept ZIP file uploads or clone directly from GitHub URLs
- **Multi-Agent Analysis**: Four specialized AI agents analyze different aspects of your code:
  - 🏗️ **Structure Agent**: Folder organization, naming conventions, project structure
  - 🔍 **Code Quality Agent**: Complexity, code smells, best practices, performance
  - 🔒 **Security Agent**: OWASP Top 10, vulnerability detection, secrets scanning
  - 📐 **Architecture Advisor**: Design patterns, scalability, technology recommendations
- **Consolidated Reports**: Generate comprehensive JSON and PDF reports
- **RESTful API**: Full API with versioning, authentication, and OpenAPI documentation
- **Async Processing**: Background worker service for non-blocking analysis

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
- [Docker](https://www.docker.com/get-started) (optional, for containerized deployment)

### Local Development

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

### Environment Variables

| Variable | Description | Default |
|----------|-------------|---------|
| `ConnectionStrings__DefaultConnection` | Database connection string | `Data Source=aar.db` |
| `UseSqlServer` | Use SQL Server instead of SQLite | `false` |
| `Azure__OpenAI__Endpoint` | Azure OpenAI endpoint URL | - |
| `Azure__OpenAI__ApiKey` | Azure OpenAI API key | - |
| `Azure__OpenAI__DeploymentName` | Azure OpenAI deployment name | `gpt-4` |
| `Azure__OpenAI__UseMock` | Use mock responses (no API calls) | `true` |
| `Azure__Storage__ConnectionString` | Azure Blob Storage connection | - |
| `BlobStorage__Provider` | Storage provider (`FileSystem` or `Azure`) | `FileSystem` |
| `QueueService__Provider` | Queue provider (`InMemory` or `Azure`) | `InMemory` |

### appsettings.json

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Data Source=aar.db"
  },
  "UseSqlServer": false,
  "Azure": {
    "OpenAI": {
      "Endpoint": "https://your-openai.openai.azure.com",
      "ApiKey": "your-api-key",
      "DeploymentName": "gpt-4",
      "UseMock": false
    }
  },
  "BlobStorage": {
    "Provider": "FileSystem",
    "BasePath": "./storage"
  }
}
```

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
- 📖 Docs: [Full Documentation](./docs/ARCHITECTURE.md)
