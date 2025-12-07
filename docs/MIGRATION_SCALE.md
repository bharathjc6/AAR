# MIGRATION_SCALE.md - Production Scaling & Resiliency Improvements

This document describes the production-grade scaling and resiliency improvements added to the AAR (Automated Architecture Review) system.

## Table of Contents
1. [Overview](#overview)
2. [New Features](#new-features)
3. [Configuration Options](#configuration-options)
4. [Database Migration](#database-migration)
5. [API Endpoints](#api-endpoints)
6. [Real-time Streaming](#real-time-streaming)
7. [Resilience Policies](#resilience-policies)
8. [Metrics & Monitoring](#metrics--monitoring)
9. [Deployment Notes](#deployment-notes)
10. [Testing](#testing)

---

## Overview

This release introduces comprehensive scaling capabilities designed to handle large codebases efficiently while maintaining system stability under load. Key improvements include:

- **Preflight Analysis** - Estimate costs and processing time before committing resources
- **Resumable Uploads** - Handle large files reliably with chunked uploads
- **Streaming Extraction** - Memory-efficient processing of large zip files
- **Job Queue with Priority** - Fair scheduling with dead-letter support
- **Polly Resilience** - Circuit breakers, retries, and timeouts for external calls
- **SignalR Streaming** - Real-time progress updates to clients
- **Comprehensive Metrics** - Track system performance and costs

---

## New Features

### 1. Config & Limits Infrastructure

New configuration options in `appsettings.json`:

```json
{
  "ScaleLimits": {
    "MaxUploadSizeMb": 500,
    "MaxFilesPerProject": 50000,
    "MaxFileSizeBytes": 10485760,
    "AutoApprovalThresholdFiles": 1000,
    "AutoApprovalThresholdTokens": 500000
  },
  "EmbeddingProcessing": {
    "BatchSize": 100,
    "MaxConcurrency": 5,
    "CostPerMillionTokens": 0.10,
    "MaxChunkSizeTokens": 512,
    "OverlapTokens": 50
  },
  "WorkerProcessing": {
    "MaxConcurrentJobs": 3,
    "CheckpointIntervalFiles": 100,
    "MaxRetryAttempts": 3,
    "RetryDelaySeconds": 30
  },
  "StoragePolicy": {
    "RetentionDays": 90,
    "CompressionEnabled": true,
    "EncryptionEnabled": true
  },
  "ResumableUpload": {
    "ChunkSizeBytes": 4194304,
    "SessionExpirationMinutes": 60,
    "MaxPartsPerUpload": 10000
  }
}
```

### 2. Preflight Endpoint

**POST** `/api/v1/preflight/analyze-stream`

Analyzes an uploaded project without processing to estimate:
- File count and size
- Token count estimation
- Processing cost estimation
- Estimated processing time
- Whether approval is required

**Response:**
```json
{
  "fileCount": 1500,
  "totalBytes": 52428800,
  "estimatedTokens": 350000,
  "estimatedCost": 0.035,
  "estimatedProcessingTimeMinutes": 15,
  "requiresApproval": false,
  "isWithinLimits": true,
  "rejectionReasons": []
}
```

### 3. Resumable Chunked Uploads

For large files, use the resumable upload API:

**POST** `/api/v1/uploads/sessions` - Create upload session
**PUT** `/api/v1/uploads/sessions/{sessionId}/parts/{partNumber}` - Upload chunk
**POST** `/api/v1/uploads/sessions/{sessionId}/finalize` - Complete upload
**GET** `/api/v1/uploads/sessions/{sessionId}` - Get session status

### 4. Streaming Extraction

The `StreamingZipExtractor` processes zip files in a memory-efficient manner:
- Streams entries directly from the archive
- Applies filters for excluded directories (`node_modules`, `.git`, `bin`, etc.)
- Reports progress via IProgress<ExtractionProgress>
- Supports cancellation

### 5. Job Queue with Priority

Jobs are queued with priority levels:
- `Critical` (3) - Production hotfixes
- `High` (2) - Priority customers
- `Normal` (1) - Standard processing
- `Low` (0) - Batch/background work

Dead-letter queue support for failed jobs with retry capabilities.

### 6. Polly Resilience Policies

Three resilience pipelines are registered:

**EmbeddingPipeline:**
- Retry with exponential backoff (3 attempts)
- Circuit breaker (5 failures = 30s break)
- Total timeout: 5 minutes

**OpenAiPipeline:**
- Retry with jitter (3 attempts)
- Circuit breaker (10 failures = 60s break)
- Rate limiter (100 requests/second)
- Total timeout: 10 minutes

**BlobStoragePipeline:**
- Retry (5 attempts)
- Timeout: 30 seconds per operation

### 7. SignalR Streaming Hub

Connect to `/hubs/analysis` for real-time updates:

```javascript
const connection = new signalR.HubConnectionBuilder()
    .withUrl("/hubs/analysis")
    .withAutomaticReconnect()
    .build();

// Subscribe to project updates
await connection.invoke("SubscribeToProject", projectId);

// Listen for events
connection.on("ProgressUpdate", (progress) => {
    console.log(`${progress.phase}: ${progress.progressPercent}%`);
});

connection.on("FindingUpdate", (finding) => {
    console.log(`Found: ${finding.finding.severity} - ${finding.finding.description}`);
});

connection.on("JobCompleted", (completion) => {
    if (completion.isSuccess) {
        console.log(`Report ready: ${completion.reportId}`);
    }
});
```

### 8. Metrics & Monitoring

Metrics available via `IMetricsService`:

| Metric | Type | Description |
|--------|------|-------------|
| `aar.jobs.queued` | Counter | Jobs added to queue |
| `aar.jobs.completed` | Counter | Successfully completed jobs |
| `aar.jobs.failed` | Counter | Failed jobs |
| `aar.jobs.duration_seconds` | Histogram | Job processing duration |
| `aar.queue.length` | Gauge | Current queue depth |
| `aar.queue.deadletter_length` | Gauge | Dead-letter queue depth |
| `aar.tokens.consumed` | Counter | Tokens used for embeddings |
| `aar.embeddings.created` | Counter | Embeddings generated |
| `aar.checkpoints.saved` | Counter | Checkpoints written |

---

## Database Migration

New tables added:

### JobCheckpoints
Stores resumable processing state:
```sql
CREATE TABLE JobCheckpoints (
    Id UNIQUEIDENTIFIER PRIMARY KEY,
    ProjectId UNIQUEIDENTIFIER NOT NULL,
    Phase NVARCHAR(100) NOT NULL,
    LastProcessedFile NVARCHAR(500),
    ProcessedFileCount INT,
    TotalFileCount INT,
    CheckpointData NVARCHAR(MAX), -- JSON
    CreatedAt DATETIME2 NOT NULL,
    UpdatedAt DATETIME2 NOT NULL
);
```

### UploadSessions
Tracks resumable upload progress:
```sql
CREATE TABLE UploadSessions (
    Id UNIQUEIDENTIFIER PRIMARY KEY,
    ApiKeyId UNIQUEIDENTIFIER NOT NULL,
    FileName NVARCHAR(500) NOT NULL,
    TotalSize BIGINT NOT NULL,
    TotalParts INT NOT NULL,
    CompletedParts INT NOT NULL,
    StoragePath NVARCHAR(1000),
    Status NVARCHAR(50),
    ExpiresAt DATETIME2 NOT NULL,
    CreatedAt DATETIME2 NOT NULL
);
```

### OrganizationQuotas
Tracks usage limits per organization:
```sql
CREATE TABLE OrganizationQuotas (
    Id UNIQUEIDENTIFIER PRIMARY KEY,
    OrganizationId NVARCHAR(100) NOT NULL,
    MonthlyTokenLimit BIGINT NOT NULL,
    MonthlyTokensUsed BIGINT NOT NULL,
    MonthlyProjectLimit INT NOT NULL,
    MonthlyProjectsUsed INT NOT NULL,
    StorageLimitBytes BIGINT NOT NULL,
    StorageUsedBytes BIGINT NOT NULL,
    PeriodStart DATETIME2 NOT NULL,
    CreatedAt DATETIME2 NOT NULL,
    UpdatedAt DATETIME2 NOT NULL
);
```

### Running the Migration

```bash
cd src/AAR.Infrastructure
dotnet ef database update --startup-project ../AAR.Api/AAR.Api.csproj
```

---

## API Endpoints

### Preflight
| Method | Endpoint | Description |
|--------|----------|-------------|
| POST | `/api/v1/preflight/analyze-stream` | Analyze uploaded file |
| POST | `/api/v1/preflight/analyze/{projectId}` | Analyze existing project |
| POST | `/api/v1/preflight/{projectId}/approve` | Approve large project |

### Uploads
| Method | Endpoint | Description |
|--------|----------|-------------|
| POST | `/api/v1/uploads/sessions` | Create upload session |
| GET | `/api/v1/uploads/sessions/{id}` | Get session status |
| PUT | `/api/v1/uploads/sessions/{id}/parts/{part}` | Upload chunk |
| POST | `/api/v1/uploads/sessions/{id}/finalize` | Complete upload |
| DELETE | `/api/v1/uploads/sessions/{id}` | Cancel upload |

---

## Resilience Policies

### Configuration

Resilience is configured via named pipelines:

```csharp
// Using resilient embedding service
services.AddSingleton<IEmbeddingService, ResilientEmbeddingService>();

// Access resilience pipeline directly
var pipeline = provider.GetRequiredService<ResiliencePipelineProvider<string>>()
    .GetPipeline("EmbeddingPipeline");

await pipeline.ExecuteAsync(async ct => 
    await embeddingService.CreateEmbeddingAsync(text, ct), 
    cancellationToken);
```

### Circuit Breaker States

| State | Description |
|-------|-------------|
| Closed | Normal operation |
| Open | Failing fast, no calls made |
| Half-Open | Testing if service recovered |

---

## Deployment Notes

### Environment Variables

```bash
# Required for production
AZURE_OPENAI_ENDPOINT=https://your-instance.openai.azure.com/
AZURE_OPENAI_KEY=your-key
AZURE_STORAGE_CONNECTION_STRING=your-storage-connection

# Optional scaling config
AAR_MAX_CONCURRENT_JOBS=5
AAR_CHECKPOINT_INTERVAL=100
AAR_AUTO_APPROVAL_THRESHOLD=1000
```

### Docker Compose

The scaling infrastructure is included in existing Docker images. No changes needed to `docker-compose.yml`.

### Health Checks

New health check endpoints:
- `/health/ready` - Includes queue and storage checks
- `/health/live` - Basic liveness

---

## Testing

### Running Scaling Tests

```bash
dotnet test --filter "FullyQualifiedName~ScalingServicesTests"
```

### Test Coverage

New tests added:
- `InMemoryJobQueueServiceTests` (7 tests)
- `InMemoryMetricsServiceTests` (7 tests)
- `JobProgressServiceTests` (3 tests)

Total test count: **108 tests** (up from 91)

---

## Migration Checklist

- [ ] Backup existing database
- [ ] Run EF migration: `AddScalingTables`
- [ ] Update `appsettings.json` with new configuration sections
- [ ] Verify health checks pass
- [ ] Test SignalR connectivity
- [ ] Configure monitoring/alerting for new metrics
- [ ] Test resumable uploads with large files
- [ ] Validate preflight estimates accuracy

---

## Service Integration Summary

The following services are now fully integrated into the processing pipeline:

| Service | Purpose | Integration Point |
|---------|---------|-------------------|
| `JobProgressService` | Real-time progress reporting via SignalR | `StartAnalysisConsumer` |
| `StreamingZipExtractor` | Memory-efficient archive extraction | `StartAnalysisConsumer` |
| `ResilientEmbeddingService` | Decorator with retry/circuit breaker | DI wrapping `IEmbeddingService` |
| `PreflightService` | Pre-analysis cost/time estimation | `PreflightController` |
| `UploadSessionService` | Resumable chunked uploads | `UploadsController` |
| `InMemoryMetricsService` | Performance metrics collection | All services |

### Removed/Deprecated Services
- `InMemoryJobQueueService` - Replaced by MassTransit message bus. Retained for testing/fallback.

### Key Integration Changes
1. **StartAnalysisConsumer** now reports progress phases (Extracting → Indexing → Analyzing → Saving) to connected clients via SignalR
2. **ResilientEmbeddingService** wraps the inner embedding service with Polly resilience policies
3. **StreamingZipExtractor** replaces direct `ZipFile.ExtractToDirectory` for memory-efficient extraction

---

## Rollback Procedure

If issues arise:

1. Revert to previous Docker image tag
2. Run migration down: `dotnet ef database update AddRagPipelineTables`
3. Remove new configuration sections from appsettings

---

## Known Limitations

1. **MassTransit In-Memory** - Current configuration uses in-memory transport. For production high-availability, configure Azure Service Bus or RabbitMQ transport.

2. **Metrics Export** - Metrics are in-memory only. Configure Application Insights or Prometheus for production monitoring.

3. **SignalR Scaling** - For multi-instance deployment, configure Redis backplane for SignalR.

4. **Rate Limiting** - Uses in-memory sliding window. For distributed rate limiting, configure Redis-backed limiter.

---

## Future Improvements

- [ ] Azure Service Bus integration for durable queuing
- [ ] Application Insights telemetry integration
- [ ] Redis backplane for SignalR scaling
- [ ] Kubernetes horizontal pod autoscaling based on queue depth
- [ ] Cost tracking and billing integration
- [ ] Distributed rate limiting with Redis

---

*Last Updated: December 2024*
*Version: 2.1.0*
