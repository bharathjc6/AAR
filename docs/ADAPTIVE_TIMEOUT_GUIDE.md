# Adaptive Timeout Configuration Guide

## Overview

The AAR system now features an **adaptive timeout strategy** for LLM provider requests, replacing the previous static 10-minute timeout approach. This provides:

- **Dynamic timeout calculation** based on request size (MaxTokens)
- **Configurable timeout strategy** with per-token scaling
- **Graceful degradation** on timeout (return partial results when available)
- **Connection pooling** optimization for HTTP efficiency
- **Streaming multiplier** for long-running streaming requests
- **Retry backoff multiplier** to give retries more time

## Why Adaptive Timeouts?

The fixed 10-minute timeout had limitations:

1. **Too generous for small requests**: Queries expecting quick answers had to wait up to 10 minutes
2. **Insufficient for large requests**: Complex analyses requiring many tokens could timeout prematurely
3. **No differentiation**: All requests, regardless of complexity, used the same threshold
4. **Limited logging**: Errors didn't explain why timeouts occurred or how to fix them

Adaptive timeouts solve these by:
- Scaling timeout based on request complexity (number of tokens requested)
- Clamping to reasonable min/max bounds
- Providing detailed logging to diagnose slow requests
- Supporting graceful partial results when inference takes too long

## Configuration

### Basic Configuration

In `appsettings.json`:

```json
{
  "AI": {
    "Local": {
      "UseAdaptiveTimeout": true,
      "TimeoutStrategy": {
        "BaseTimeoutSeconds": 60,
        "PerTokenTimeoutMs": 10.0,
        "MaxTimeoutSeconds": 600,
        "MinTimeoutSeconds": 30,
        "EnableConnectionPooling": true,
        "KeepAliveTimeoutSeconds": 300,
        "EnableGracefulDegradation": true,
        "StreamingTimeoutMultiplier": 1.5,
        "RetryTimeoutMultiplier": 1.2
      }
    }
  }
}
```

### Configuration Parameters

| Parameter | Default | Description |
|-----------|---------|-------------|
| **BaseTimeoutSeconds** | 60 | Base timeout for all requests (in seconds) |
| **PerTokenTimeoutMs** | 10.0 | Additional timeout per requested token (in milliseconds) |
| **MaxTimeoutSeconds** | 600 | Maximum timeout ceiling (10 minutes) |
| **MinTimeoutSeconds** | 30 | Minimum timeout floor (to avoid too-fast timeouts) |
| **EnableConnectionPooling** | true | Enable HTTP connection pooling for efficiency |
| **KeepAliveTimeoutSeconds** | 300 | Keep-alive timeout for persistent connections |
| **EnableGracefulDegradation** | true | Return partial results on timeout if available |
| **StreamingTimeoutMultiplier** | 1.5 | Multiply timeout by this for streaming requests (1.5x base) |
| **RetryTimeoutMultiplier** | 1.2 | Multiply timeout per retry attempt (reserved for future use) |

## Timeout Calculation

The adaptive timeout for a request is calculated as:

```
timeout = base + (maxTokens * perTokenTimeout)
timeout = clamp(timeout, min, max)
timeout *= (1.5x if streaming, 1.0x otherwise)
```

### Examples

**Small request (100 tokens)**:
- base: 60s + (100 × 10ms) = 60s + 1s = **61 seconds**

**Medium request (512 tokens)**:
- base: 60s + (512 × 10ms) = 60s + 5.12s = **65 seconds**

**Large request (2048 tokens)**:
- base: 60s + (2048 × 10ms) = 60s + 20.48s = **80 seconds**

**Very large request (4096 tokens)**:
- base: 60s + (4096 × 10ms) = 60s + 40.96s = **101 seconds**

**Very large streaming request (4096 tokens, streaming)**:
- base: (60s + 40.96s) × 1.5 = 101s × 1.5 = **151 seconds**

**Clamped at max (16k tokens)**:
- calculated: 60s + (16000 × 10ms) = 60s + 160s = 220s
- clamped to max: **600 seconds** (10 minutes)

## Graceful Degradation

When `EnableGracefulDegradation` is true:

### Non-Streaming Requests
Returns an error with helpful troubleshooting guidance instead of a generic timeout.

### Streaming Requests
Returns the partial content received before the timeout, marked as `"incomplete"`:

```csharp
// Before timeout: LLM streams "The quick brown fox jumps..."
// Timeout occurs at 45 seconds
// Returns: content="The quick brown fox jumps...", finishReason="incomplete"
```

This allows consumers to:
- Display partial analysis results to users
- Cache partial findings for later review
- Avoid losing work already completed

## Troubleshooting

### "Request timed out after X seconds"

**Root causes**:
1. LLM model is too slow (running on CPU, not GPU)
2. System is under high load (CPU/memory contention)
3. Network latency to LLM service is high
4. Requested token count is too large

**Solutions** (in order of preference):

1. **Increase MaxTimeoutSeconds** (if temporarily slow):
   ```json
   "MaxTimeoutSeconds": 900  // 15 minutes
   ```

2. **Reduce MaxTokens** (for faster responses):
   ```json
   "MaxTokens": 2048  // from 4096
   ```

3. **Switch to a faster model** (if available):
   ```json
   "LLMModel": "qwen2.5-coder:1.5b"  // smaller, faster model
   ```

4. **Reduce PerTokenTimeoutMs** (be careful - may cause more timeouts):
   ```json
   "PerTokenTimeoutMs": 5.0  // from 10.0
   ```

5. **Enable GPU acceleration** for Ollama (best for performance)

### Streaming requests timeout more than non-streaming

This is expected due to `StreamingTimeoutMultiplier: 1.5`. Streaming has additional overhead from:
- Establishing streaming connection
- Chunking protocol overhead
- Event processing for each chunk

If streaming timeouts occur frequently, adjust:

```json
"StreamingTimeoutMultiplier": 1.3  // reduce from 1.5
// or
"MaxTimeoutSeconds": 900  // increase ceiling
```

### High token count = very long timeout

For requests with `MaxTokens: 16000` (near ceiling), the timeout reaches the 10-minute limit. This is intentional to prevent premature timeouts for very complex analysis.

If this is too slow, use a shorter `MaxTokens` value or split the analysis into multiple smaller requests.

## Logging

The system logs timeout events with diagnostic information:

```
Sending LLM request to Ollama (model: qwen2.5-coder:7b, maxTokens: 2048, timeout: 80s)
Calculated adaptive timeout: 80s (base: 60s, tokens: 2048, streaming: False)

// On timeout:
LLM request timed out by resilience pipeline after 80234ms. 
Adaptive timeout was: 80s. Consider increasing TimeoutStrategy.MaxTimeoutSeconds.
```

**Key fields**:
- `timeout`: Calculated adaptive timeout for this request
- `maxTokens`: Requested token limit
- `streaming`: Whether this was a streaming request
- `Duration`: Actual wall-clock time before timeout

## Performance Impact

Adaptive timeouts provide better resource utilization:

1. **Faster feedback for quick tasks**: Small requests don't wait the full 10 minutes
2. **More graceful degradation**: Partial results on long-running tasks
3. **Better error messages**: Clear guidance when timeouts occur
4. **Connection pooling**: Reduced TCP handshake overhead

No measurable performance degradation vs. static timeouts.

## Migration from Static Timeouts

The system automatically uses adaptive timeouts if:
1. `AI.Local.UseAdaptiveTimeout: true` (default)
2. `AI.Local.TimeoutStrategy` is configured (has defaults)

**To disable and revert to static timeouts**:

```json
"AI": {
  "Local": {
    "UseAdaptiveTimeout": false,
    "TimeoutSeconds": 600  // reverts to static 10-minute timeout
  }
}
```

## Future Enhancements

Potential improvements to the timeout strategy:

1. **Historical performance tracking**: Measure per-model inference speed and auto-adjust baseTimeoutSeconds
2. **Per-agent timeouts**: Different timeout strategies for different analysis types
3. **Request profiling**: Track which requests timeout and suggest configuration changes
4. **Retry backoff multiplier**: Use `RetryTimeoutMultiplier` to give retries exponentially longer timeouts
5. **Dynamic base timeout**: Scale based on recent system load or inference queue depth

## Related Configuration

Also see:

- [LOCAL_AI_SETUP.md](LOCAL_AI_SETUP.md) - Local AI deployment
- [RESILIENCE_POLICIES.md](RESILIENCE_POLICIES.md) - Polly resilience pipeline (if exists)
- [Configuration/AIProviderOptions.cs](../src/AAR.Application/Configuration/AIProviderOptions.cs) - Configuration classes

## Questions?

If you encounter timeout issues:

1. Check the logs for "Calculated adaptive timeout" entries
2. Verify your `TimeoutStrategy` configuration in appsettings.json
3. Review the actual timeout duration vs. request complexity
4. Consider whether MaxTokens is appropriate for your analysis type
5. Check Ollama GPU/CPU usage if available

---

**Last Updated**: 2025-12-15  
**Feature**: Adaptive Timeout Strategy for LLM Providers  
**Status**: Production-Ready
