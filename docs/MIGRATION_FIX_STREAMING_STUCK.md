# Migration: Fix Streaming Batch Processing Stuck Issue

## Issue Summary

**Problem**: Worker process gets stuck at "Processing streaming batch 21/33" when scanning large repositories (specifically observed with Azure AD sample repos containing ~330 files).

**Root Cause**: Multiple timeout and deadlock conditions in the embedding service rate limiting and lack of watchdog/heartbeat mechanism for detecting stuck operations.

## Changes Made

### 1. Fixed Rate Limiter Deadlock in ResilientEmbeddingService

**File**: `src/AAR.Infrastructure/Services/Embedding/ResilientEmbeddingService.cs`

**Problem**: The rate limiter could wait indefinitely due to:
- 2-minute semaphore waits that blocked processing
- 120 iteration wait loop (2 minutes max) that still could cause stalls

**Fix**:
- Reduced semaphore wait from 2 minutes to 30 seconds
- Reduced max wait iterations from 120 to 30 (30 seconds max)
- Added cancellation token checking inside the wait loop
- Reset rate limit period when forcing through after max wait
- Enhanced logging with elapsed time and iteration counts

```csharp
// Before: 2 minute wait could block entire batch
acquired = await _rateLimiter.WaitAsync(TimeSpan.FromMinutes(2), cancellationToken);

// After: 30 second max wait with clear logging
acquired = await _rateLimiter.WaitAsync(TimeSpan.FromSeconds(30), cancellationToken);
```

### 2. Added BatchProcessingWatchdog Service

**File**: `src/AAR.Infrastructure/Services/Watchdog/BatchProcessingWatchdog.cs`

A new `BackgroundService` that monitors batch processing and detects stuck operations:

**Features**:
- Tracks all active indexing operations by project ID
- Receives heartbeats from processing loops
- Detects stuck operations based on:
  - No heartbeat for configurable duration (default: 2 minutes)
  - Exceeded max project duration (default: 10 minutes)
- Optionally auto-cancels stuck operations
- Exposes metrics for monitoring

**Configuration** (appsettings.json):
```json
{
  "Watchdog": {
    "Enabled": true,
    "CheckIntervalSeconds": 30,
    "MaxProjectDurationSeconds": 600,
    "MaxHeartbeatIntervalSeconds": 120,
    "AutoCancelStuck": true,
    "StuckDetectionThreshold": 2
  }
}
```

### 3. Integrated Watchdog into RetrievalOrchestrator

**File**: `src/AAR.Infrastructure/Services/Retrieval/RetrievalOrchestrator.cs`

**Changes**:
- Added `IBatchProcessingWatchdog` dependency injection
- Track each project indexing operation with linked CancellationToken
- Send heartbeats at critical points:
  - After each file read
  - After chunking completes
  - After each embedding batch
  - After each DB save
  - After each vector store index
- Update phase for visibility:
  - "Batch X/Y: Loading files"
  - "Batch X/Y: Chunking"
  - "Batch X/Y: Embeddings N"
  - "Batch X/Y: Saving DB"
  - "Batch X/Y: Vector indexing"
- Call `Complete()` when indexing finishes

### 4. Reduced Embedding Timeout

**File**: `src/AAR.Infrastructure/Services/Retrieval/RetrievalOrchestrator.cs`

- Reduced embedding batch timeout from 2 minutes to 1 minute
- Linked all sub-operation CancellationTokens to the watchdog's linked token

## DI Registration

**File**: `src/AAR.Infrastructure/DependencyInjection.cs`

```csharp
// Batch processing watchdog for stuck detection
services.Configure<WatchdogOptions>(configuration.GetSection("Watchdog"));
services.AddSingleton<IBatchProcessingWatchdog, BatchProcessingWatchdog>();
services.AddHostedService(sp => (BatchProcessingWatchdog)sp.GetRequiredService<IBatchProcessingWatchdog>());
```

## Tests Added

### Unit Tests

**File**: `tests/AAR.Tests/Unit/Watchdog/BatchProcessingWatchdogTests.cs`
- 10 tests covering all watchdog functionality

**File**: `tests/AAR.Tests/Unit/Embedding/ResilientEmbeddingServiceTests.cs`
- 5 tests ensuring rate limiting doesn't cause deadlocks

### Integration Tests

**File**: `tests/AAR.Tests/Integration/WatchdogIntegrationTests.cs`
- 5 tests verifying watchdog integration with RetrievalOrchestrator

### Diagnostic Tests

**File**: `tests/AAR.Tests/Diagnostics/StreamingBatchStuckDiagnosticTests.cs`
- 7 tests simulating the exact conditions that caused batch 21/33 to stick

## Verification

Run all related tests:
```bash
dotnet test --filter "FullyQualifiedName~Watchdog|FullyQualifiedName~ResilientEmbedding|FullyQualifiedName~StreamingBatchStuck"
```

Expected results:
- 27 tests pass
- No tests fail
- Batch 33/33 completes successfully in diagnostic tests

## Rollback Plan

If issues occur:
1. Set `Watchdog:Enabled` to `false` in appsettings.json
2. The watchdog will disable itself but won't affect normal processing
3. The rate limiting improvements in ResilientEmbeddingService will still be active

## Monitoring

Watch for these log messages:

**Healthy operation**:
```
[Information] Processing streaming batch X/Y (N files)
[Debug] Batch X: Embeddings generated, preparing DB entities...
```

**Potential issues** (watchdog warning):
```
[Warning] Watchdog: Project {ProjectId} appears STUCK: no heartbeat for Xs
```

**Recovery action** (if auto-cancel enabled):
```
[Error] Watchdog: CANCELLING stuck project {ProjectId}
```

## Metrics Added

- `watchdog_stuck_detected` - Counter for stuck detections
- `watchdog_project_cancelled` - Counter for auto-cancelled projects
- `indexing_duration_seconds` - Histogram of successful indexing durations
