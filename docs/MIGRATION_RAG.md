# RAG Integration Migration Guide

This document describes the changes introduced for RAG-aware file routing and memory hardening in the AAR Worker.

## Overview

The RAG integration introduces intelligent file routing that determines how each file should be processed during analysis:

1. **Direct Send** (files < 10KB): Sent directly to LLM for analysis
2. **RAG Chunks** (files 10KB - 200KB): Chunked, embedded, and processed via RAG pipeline
3. **Skip** (files > 200KB or binary): Skipped to prevent memory exhaustion

## New Configuration Options

Add the following sections to `appsettings.json`:

```json
{
  "RagProcessing": {
    "DirectSendThresholdBytes": 10240,
    "RagChunkThresholdBytes": 204800,
    "ChunkSizeBytes": 4096,
    "ChunkOverlapBytes": 512,
    "MaxChunksPerFile": 100,
    "TopKChunksForReasoning": 10,
    "EnableHighRiskPrioritization": true,
    "HighRiskSimilarityThreshold": 0.75
  },
  "MemoryManagement": {
    "MaxWorkerMemoryMB": 4096,
    "MemoryWarningThresholdPercent": 80,
    "MemoryPauseThresholdPercent": 90,
    "MemoryCheckIntervalSeconds": 5,
    "MinFreeDiskSpaceMB": 1024,
    "MaxTempFolderSizeMB": 2048,
    "EnableAggressiveGC": true,
    "GCIntervalBatches": 3
  },
  "Concurrency": {
    "EmbeddingConcurrency": 4,
    "ReasoningConcurrency": 2,
    "FileReadConcurrency": 8,
    "EmbeddingBatchSize": 32,
    "ChunkProcessingConcurrency": 4
  }
}
```

### Configuration Details

| Setting | Default | Description |
|---------|---------|-------------|
| `DirectSendThresholdBytes` | 10240 (10KB) | Files smaller than this are sent directly to LLM |
| `RagChunkThresholdBytes` | 204800 (200KB) | Files larger than this are skipped entirely |
| `MaxWorkerMemoryMB` | 4096 | Maximum memory before triggering pause |
| `EmbeddingConcurrency` | 4 | Max parallel embedding API calls |
| `ReasoningConcurrency` | 2 | Max parallel chat completion calls |

## Environment Variables

For production deployment, set these environment variables:

```bash
# RAG Processing
RagProcessing__DirectSendThresholdBytes=10240
RagProcessing__RagChunkThresholdBytes=204800
RagProcessing__EnableHighRiskPrioritization=true

# Memory Management
MemoryManagement__MaxWorkerMemoryMB=4096
MemoryManagement__MemoryWarningThresholdPercent=80
MemoryManagement__MemoryPauseThresholdPercent=90

# Concurrency
Concurrency__EmbeddingConcurrency=4
Concurrency__ReasoningConcurrency=2
```

## New Files Created

### Configuration Classes
- `AAR.Application/Configuration/RagProcessingOptions.cs` - Configuration POCOs for RAG processing, memory management, and concurrency

### DTOs
- `AAR.Application/DTOs/FileAnalysisPlanDto.cs` - DTOs for analysis planning and routing decisions

### Interfaces
- `AAR.Application/Interfaces/IFileAnalysisRouter.cs` - Router interface for file routing decisions
- `AAR.Application/Interfaces/IRagAwareAgentOrchestrator.cs` - Extended orchestrator interface with plan support

### Services
- `AAR.Infrastructure/Services/Routing/FileAnalysisRouter.cs` - Routes files based on size thresholds
- `AAR.Infrastructure/Services/Routing/RagRiskFilter.cs` - Identifies high-risk files using embedding similarity
- `AAR.Infrastructure/Services/Memory/TempFileChunkWriter.cs` - Disk-backed chunk storage
- `AAR.Infrastructure/Services/Memory/MemoryMonitor.cs` - Memory usage monitoring
- `AAR.Infrastructure/Services/Memory/ConcurrencyLimiter.cs` - Bounded concurrency control

### Orchestrator
- `AAR.Worker/Agents/RagAwareAgentOrchestrator.cs` - Agent orchestrator with routing plan support

## Modified Files

### Domain
- `AAR.Domain/Interfaces/IUnitOfWork.cs` - Added `JobCheckpoints` property

### Infrastructure
- `AAR.Infrastructure/Persistence/UnitOfWork.cs` - Implemented `JobCheckpoints` repository
- `AAR.Infrastructure/DependencyInjection.cs` - Registered new services

### Application
- `AAR.Application/DTOs/PreflightDto.cs` - Added routing breakdown fields

### Worker
- `AAR.Worker/Consumers/StartAnalysisConsumer.cs` - Integrated file routing

## Database Migrations

No new database migrations required. The existing `JobCheckpoints` table from the `AddScalingTables` migration is used.

## API Changes

### Preflight Endpoint Response

The preflight endpoint now returns additional fields:

```json
{
  "totalFiles": 150,
  "totalSizeBytes": 5242880,
  "estimatedTokens": 50000,
  "estimatedCost": 0.25,
  "directSendCount": 120,
  "ragChunkCount": 25,
  "skippedCount": 5,
  "skippedFiles": [
    {
      "filePath": "large-data.json",
      "reason": "Exceeds size threshold (2.5 MB > 200 KB)"
    }
  ],
  "fileTypeBreakdown": {
    ".cs": 80,
    ".ts": 45,
    ".json": 25
  }
}
```

## Local Development

### Running with RAG routing

1. Ensure the API is running:
   ```powershell
   cd src/AAR.Api
   dotnet run
   ```

2. Run the Worker:
   ```powershell
   cd src/AAR.Worker
   dotnet run
   ```

3. Submit a project for analysis using the API

### Testing thresholds

To test different file routing scenarios, modify `appsettings.Development.json`:

```json
{
  "RagProcessing": {
    "DirectSendThresholdBytes": 5120,  // 5KB for testing
    "RagChunkThresholdBytes": 51200    // 50KB for testing
  }
}
```

### Monitoring memory usage

The Worker logs memory usage at regular intervals:

```
[INF] Memory check: Current=1024MB, Max=4096MB, Usage=25%
```

When memory pressure is high:

```
[WRN] Memory warning: Usage at 85%, triggering GC
```

When memory threshold is exceeded:

```
[ERR] Memory threshold exceeded (92% > 90%), pausing processing
```

## Checkpointing and Recovery

Jobs that are paused due to memory pressure can be resumed. The checkpoint stores:

- Files already processed
- Current processing phase
- Serialized analysis plan

To check for paused jobs:

```sql
SELECT * FROM JobCheckpoints 
WHERE Status = 'PausedOnResource'
ORDER BY UpdatedAt DESC;
```

## Troubleshooting

### Out of Memory Issues

1. Reduce `MaxWorkerMemoryMB` threshold
2. Lower `EmbeddingConcurrency` and `ReasoningConcurrency`
3. Reduce `RagChunkThresholdBytes` to skip larger files

### Files Being Skipped Unexpectedly

1. Check the preflight response for `skippedFiles`
2. Increase `RagChunkThresholdBytes` if files should be processed
3. Set `AllowLargeFiles: true` in the analysis request

### Slow Processing

1. Increase `EmbeddingConcurrency` (max recommended: 8)
2. Increase `ReasoningConcurrency` (max recommended: 4)
3. Enable `EnableAggressiveGC` to free memory between batches

## Performance Recommendations

| Scenario | DirectSend | RagChunk | Concurrency |
|----------|------------|----------|-------------|
| Low memory (2GB) | 5KB | 50KB | Embed: 2, Reason: 1 |
| Standard (4GB) | 10KB | 200KB | Embed: 4, Reason: 2 |
| High memory (8GB+) | 20KB | 500KB | Embed: 8, Reason: 4 |

## Security Considerations

1. Temp files are written to system temp directory with unique names
2. Temp files are cleaned up after processing completes
3. Memory monitor prevents unbounded memory growth
4. All file paths are validated before processing

## Rollback

To disable RAG routing and revert to the previous behavior:

1. Remove or comment out the `RagProcessing` configuration section
2. The system will fall back to processing all files directly

---

*Last updated: December 2024*
