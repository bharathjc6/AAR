# AAR Architecture Documentation

## Overview

The Autonomous Architecture Reviewer (AAR) is designed following **Clean Architecture** principles, ensuring separation of concerns, testability, and maintainability.

## System Architecture

```mermaid
graph TB
    subgraph "External Clients"
        CLI[CLI Tools]
        UI[Web UI]
        API_CLIENT[API Clients]
    end
    
    subgraph "API Layer"
        GATEWAY[API Gateway / Load Balancer]
        API[AAR.Api]
    end
    
    subgraph "Processing Layer"
        WORKER[AAR.Worker]
        AGENTS[Analysis Agents]
    end
    
    subgraph "Storage Layer"
        DB[(Database)]
        BLOB[Blob Storage]
        QUEUE[Message Queue]
    end
    
    subgraph "External Services"
        OPENAI[Azure OpenAI]
        GIT[GitHub API]
    end
    
    CLI --> GATEWAY
    UI --> GATEWAY
    API_CLIENT --> GATEWAY
    GATEWAY --> API
    
    API --> DB
    API --> BLOB
    API --> QUEUE
    
    QUEUE --> WORKER
    WORKER --> AGENTS
    AGENTS --> OPENAI
    
    WORKER --> DB
    WORKER --> BLOB
    
    API --> GIT
```

## Layer Dependencies

```mermaid
graph TD
    API[AAR.Api] --> APP[AAR.Application]
    WORKER[AAR.Worker] --> APP
    APP --> DOMAIN[AAR.Domain]
    INFRA[AAR.Infrastructure] --> APP
    INFRA --> DOMAIN
    API --> INFRA
    WORKER --> INFRA
    APP --> SHARED[AAR.Shared]
    DOMAIN --> SHARED
    INFRA --> SHARED
    
    style DOMAIN fill:#e1f5fe
    style APP fill:#fff3e0
    style INFRA fill:#f3e5f5
    style API fill:#e8f5e9
    style WORKER fill:#e8f5e9
    style SHARED fill:#fce4ec
```

## Project Structure

### AAR.Shared
Common utilities shared across all projects:
- `Result<T>` - Result pattern for error handling without exceptions
- `Error` - Strongly-typed error representation
- `PagedResult<T>` - Pagination wrapper

### AAR.Domain
Core business entities and interfaces:
- **Entities**: `Project`, `Report`, `ReviewFinding`, `FileRecord`, `ApiKey`
- **Value Objects**: `LineRange`, `FileMetrics`
- **Enums**: `Severity`, `FindingCategory`, `ProjectStatus`, `AgentType`
- **Interfaces**: Repository contracts (`IProjectRepository`, etc.)

### AAR.Application
Business logic and orchestration:
- **Services**: `ProjectService`, `ReportService`, `ReportAggregator`
- **DTOs**: Data transfer objects for API communication
- **Interfaces**: External service contracts (`IBlobStorageService`, `IOpenAiService`, etc.)
- **Validators**: FluentValidation validators

### AAR.Infrastructure
External implementations:
- **Persistence**: EF Core `DbContext`, Repository implementations
- **Storage**: `FileSystemBlobStorage`, `AzureBlobStorage`
- **Queue**: `InMemoryQueueService`, `AzureQueueService`
- **AI**: `AzureOpenAiService` with mock mode
- **Git**: `GitService` for cloning repositories

### AAR.Api
REST API:
- **Controllers**: `ProjectsController`, `ReportsController`
- **Middleware**: `ExceptionHandlingMiddleware`, `ApiKeyAuthMiddleware`
- Health checks, OpenAPI/Swagger

### AAR.Worker
Background processing:
- **AnalysisWorker**: Hosted service polling the queue
- **Agents**: `StructureAgent`, `CodeQualityAgent`, `SecurityAgent`, `ArchitectureAdvisorAgent`
- **AgentOrchestrator**: Coordinates agent execution

## Data Flow

```mermaid
sequenceDiagram
    participant Client
    participant API
    participant BlobStorage
    participant Queue
    participant Worker
    participant Agents
    participant OpenAI
    participant Database

    Client->>API: POST /projects
    API->>Database: Create Project
    API-->>Client: Project Created

    Client->>API: POST /projects/{id}/upload
    API->>BlobStorage: Store ZIP
    API->>Queue: Enqueue AnalysisJob
    API->>Database: Update Status → Queued
    API-->>Client: Upload Accepted

    Worker->>Queue: Dequeue Job
    Worker->>Database: Update Status → Analyzing
    Worker->>BlobStorage: Download Files
    
    loop For Each Agent
        Worker->>Agents: Analyze
        Agents->>OpenAI: Get AI Analysis
        OpenAI-->>Agents: Findings
        Agents-->>Worker: Agent Findings
    end
    
    Worker->>Database: Save Findings
    Worker->>Database: Create Report
    Worker->>Database: Update Status → Completed
    
    Client->>API: GET /reports/project/{id}
    API->>Database: Fetch Report
    API-->>Client: Report JSON/PDF
```

## Agent Architecture

```mermaid
graph LR
    subgraph "Agent Orchestrator"
        ORCH[AgentOrchestrator]
    end
    
    subgraph "Analysis Agents"
        SA[StructureAgent]
        CQA[CodeQualityAgent]
        SEC[SecurityAgent]
        AAA[ArchitectureAdvisorAgent]
    end
    
    subgraph "Services"
        OAI[OpenAI Service]
        METRICS[Metrics Service]
    end
    
    ORCH --> SA
    ORCH --> CQA
    ORCH --> SEC
    ORCH --> AAA
    
    SA --> OAI
    SA --> METRICS
    CQA --> OAI
    CQA --> METRICS
    SEC --> OAI
    AAA --> OAI
```

### Agent Responsibilities

| Agent | Focus Areas | Severity Range |
|-------|-------------|----------------|
| **StructureAgent** | Folder organization, naming, project layout | Info → Medium |
| **CodeQualityAgent** | Complexity, code smells, best practices | Low → High |
| **SecurityAgent** | OWASP Top 10, secrets, vulnerabilities | Medium → Critical |
| **ArchitectureAdvisorAgent** | Patterns, scalability, design | Info → High |

## Database Schema

```mermaid
erDiagram
    Project ||--o{ FileRecord : contains
    Project ||--o{ ReviewFinding : has
    Project ||--o| Report : generates
    
    Project {
        guid Id PK
        string Name
        string Description
        enum Status
        string StoragePath
        string GitUrl
        datetime CreatedAt
        datetime UpdatedAt
    }
    
    FileRecord {
        guid Id PK
        guid ProjectId FK
        string FilePath
        string Language
        int LinesOfCode
        int Complexity
    }
    
    ReviewFinding {
        guid Id PK
        guid ProjectId FK
        guid ReportId FK
        enum AgentType
        string Title
        string Description
        enum Severity
        enum Category
        string FilePath
        string CodeSnippet
        string Suggestion
    }
    
    Report {
        guid Id PK
        guid ProjectId FK
        int OverallScore
        string Summary
        int CriticalCount
        int HighCount
        int MediumCount
        int LowCount
        datetime GeneratedAt
    }
    
    ApiKey {
        guid Id PK
        string Name
        string KeyHash
        string UserId
        bool IsActive
        datetime ExpiresAt
    }
```

## Deployment Architecture

### Development
```mermaid
graph LR
    subgraph "Local Machine"
        API[API :5000]
        WORKER[Worker]
        SQLITE[(SQLite)]
        FS[File System Storage]
    end
    
    API --> SQLITE
    API --> FS
    WORKER --> SQLITE
    WORKER --> FS
```

### Production (Azure)
```mermaid
graph TB
    subgraph "Azure"
        subgraph "Compute"
            ACA[Azure Container Apps]
        end
        
        subgraph "Data"
            SQL[(Azure SQL)]
            BLOB[Azure Blob Storage]
            QUEUE[Azure Queue Storage]
        end
        
        subgraph "AI"
            AOAI[Azure OpenAI]
        end
        
        subgraph "Networking"
            APIM[API Management]
            FD[Front Door CDN]
        end
    end
    
    FD --> APIM
    APIM --> ACA
    ACA --> SQL
    ACA --> BLOB
    ACA --> QUEUE
    ACA --> AOAI
```

## Security Architecture

```mermaid
graph TD
    subgraph "Security Layers"
        L1[API Key Authentication]
        L2[Input Validation]
        L3[Rate Limiting]
        L4[TLS Encryption]
        L5[Secrets Management]
    end
    
    REQUEST[Incoming Request] --> L4
    L4 --> L1
    L1 --> L3
    L3 --> L2
    L2 --> HANDLER[Request Handler]
    
    L5 --> CONFIG[App Configuration]
```

### Security Controls

1. **Authentication**: API Key validation via `X-API-Key` header
2. **Authorization**: Key-based access control
3. **Input Validation**: FluentValidation on all inputs
4. **Secrets**: Environment variables / Azure Key Vault
5. **Transport**: TLS 1.2+ required
6. **Logging**: Structured logging with PII redaction

## Scalability Considerations

### Horizontal Scaling
- **API**: Stateless, scales horizontally behind load balancer
- **Worker**: Multiple instances can process queue concurrently
- **Database**: Connection pooling, read replicas for queries

### Performance Optimizations
- Async/await throughout for non-blocking I/O
- Pagination for large result sets
- File streaming for uploads/downloads
- Caching headers for static responses

### Bottleneck Mitigation
| Component | Strategy |
|-----------|----------|
| Database | Connection pooling, indexing, query optimization |
| OpenAI API | Rate limiting, retry with exponential backoff |
| Blob Storage | CDN for downloads, chunked uploads |
| Queue | Visibility timeout, dead-letter queue |

## Error Handling Strategy

```mermaid
graph TD
    ERROR[Error Occurs] --> TYPE{Error Type?}
    
    TYPE -->|Validation| VALIDATION[400 Bad Request]
    TYPE -->|Not Found| NOTFOUND[404 Not Found]
    TYPE -->|Unauthorized| UNAUTH[401 Unauthorized]
    TYPE -->|Conflict| CONFLICT[409 Conflict]
    TYPE -->|Unexpected| INTERNAL[500 Internal Error]
    
    VALIDATION --> LOG[Log Warning]
    NOTFOUND --> LOG
    UNAUTH --> LOG
    CONFLICT --> LOG
    INTERNAL --> ALERT[Log Error + Alert]
    
    LOG --> RESPONSE[Return Error Response]
    ALERT --> RESPONSE
```

## Monitoring & Observability

### Logging
- **Serilog** with structured logging
- Correlation IDs across requests
- Log levels: Debug, Info, Warning, Error

### Health Checks
- `/health` - Overall system health
- `/health/ready` - Readiness probe
- `/health/live` - Liveness probe

### Metrics (Future)
- Request duration histogram
- Queue depth gauge
- Agent execution time
- Error rate counters

## Future Enhancements

1. **Real-time Updates**: SignalR for live analysis progress
2. **Multi-tenant**: Organization-based isolation
3. **Custom Rules**: User-defined analysis rules
4. **Integration**: GitHub Actions, Azure DevOps integration
5. **Caching**: Redis for report caching
6. **ML Enhancement**: Fine-tuned models for domain-specific analysis
