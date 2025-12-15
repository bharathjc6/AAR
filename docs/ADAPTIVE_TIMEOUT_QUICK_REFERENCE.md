# Quick Reference: Adaptive Timeout Configuration

## TL;DR - Default Configuration Works Well

The default configuration is tuned for most deployments. No changes needed unless you experience timeouts.

```json
"TimeoutStrategy": {
  "BaseTimeoutSeconds": 60,
  "PerTokenTimeoutMs": 10.0,
  "MaxTimeoutSeconds": 600,
  "MinTimeoutSeconds": 30,
  "EnableGracefulDegradation": true,
  "StreamingTimeoutMultiplier": 1.5
}
```

## If Requests Are Timing Out

**Quick fix**: Increase `MaxTimeoutSeconds`

```json
"MaxTimeoutSeconds": 900  // 15 minutes instead of 10
```

**Better fix**: Check what's slow

1. Look at logs for: `Calculated adaptive timeout: Xs`
2. Check if that's actually enough time (look for actual duration before timeout)
3. If model is slow, increase base timeout or max timeout
4. If specific requests are slow, try reducing `MaxTokens` in request

## If Timeouts Seem Too Long

**Quick fix**: Reduce `MaxTimeoutSeconds` or `BaseTimeoutSeconds`

```json
"MaxTimeoutSeconds": 300,     // 5 minutes instead of 10
"BaseTimeoutSeconds": 45      // 45 seconds instead of 60
```

**Warning**: May cause more timeouts on slow systems!

## Configuration Scenarios

### Conservative (Slow Systems, CPU Inference)
```json
"BaseTimeoutSeconds": 120,
"PerTokenTimeoutMs": 15.0,
"MaxTimeoutSeconds": 900,
"StreamingTimeoutMultiplier": 2.0
```

### Balanced (Recommended Default)
```json
"BaseTimeoutSeconds": 60,
"PerTokenTimeoutMs": 10.0,
"MaxTimeoutSeconds": 600,
"StreamingTimeoutMultiplier": 1.5
```

### Aggressive (Fast Systems, GPU Inference)
```json
"BaseTimeoutSeconds": 30,
"PerTokenTimeoutMs": 5.0,
"MaxTimeoutSeconds": 300,
"StreamingTimeoutMultiplier": 1.2
```

## Quick Timeout Calculator

For your expected token count:

```
timeout = 60 + (tokens × 10 ÷ 1000) seconds
```

Examples:
- 100 tokens: 60 + 1 = **61s**
- 512 tokens: 60 + 5 = **65s**
- 1024 tokens: 60 + 10 = **70s**
- 2048 tokens: 60 + 20 = **80s**
- 4096 tokens: 60 + 41 = **~101s** (but clamped at 600s max)

## Disable Adaptive Timeouts (Revert to Static)

```json
"UseAdaptiveTimeout": false,
"TimeoutSeconds": 600
```

## Check What's Configured

Look in your `appsettings.json` under:

```
"AI" → "Local" → "TimeoutStrategy"
```

Or check logs during startup for: `"OllamaLLMProvider initialized..."`

## Common Issues & Fixes

| Issue | Check | Fix |
|-------|-------|-----|
| Requests always timeout | Logs show `Timeout: 60s` but actual time > 60s | Increase `BaseTimeoutSeconds` or `MaxTimeoutSeconds` |
| Only large requests timeout | `MaxTokens` is high (4096+) and hits max timeout | Reduce `MaxTokens` or increase `MaxTimeoutSeconds` |
| Quick requests wait too long | `MaxTimeoutSeconds: 600` | Reduce to 300-300s for faster feedback |
| Streaming always times out | Check `StreamingTimeoutMultiplier: 1.5` is applied | Increase multiplier or base timeout |
| Errors say "timed out by resilience pipeline" | This is expected on actual timeout | See timeout fix above |

## Connection Pooling (Network Optimization)

Already enabled by default:
```json
"EnableConnectionPooling": true,
"KeepAliveTimeoutSeconds": 300
```

Only disable if troubleshooting network issues:
```json
"EnableConnectionPooling": false
```

## Graceful Degradation (Partial Results)

Enabled by default - if a request times out:
- **Streaming**: Returns partial content received so far
- **Non-streaming**: Returns error with suggestions

Disable if you want failures instead:
```json
"EnableGracefulDegradation": false
```

## Where to Find Help

| Question | Resource |
|----------|----------|
| Full configuration guide | [ADAPTIVE_TIMEOUT_GUIDE.md](ADAPTIVE_TIMEOUT_GUIDE.md) |
| Technical implementation | [TIMEOUT_IMPROVEMENT_SUMMARY.md](TIMEOUT_IMPROVEMENT_SUMMARY.md) |
| Source code | `src/AAR.Infrastructure/Services/AI/OllamaLLMProvider.cs` |
| Configuration classes | `src/AAR.Application/Configuration/AIProviderOptions.cs` |

## Testing Your Configuration

1. Add a simple test request (100 tokens)
2. Check logs for: `Calculated adaptive timeout: 61s`
3. Verify request completes within ~61 seconds
4. Add a larger request (2048 tokens)
5. Check logs for: `Calculated adaptive timeout: 80s`

## Emergency: Disable Entirely

If timeouts are causing problems and you need quick relief:

```json
"AI": {
  "Local": {
    "UseAdaptiveTimeout": false,
    "TimeoutSeconds": 1200,
    "MaxRetries": 1
  }
}
```

This gives you:
- Static 20-minute timeout
- Single retry instead of multiple
- No adaptive calculation overhead

Then tune configuration later.

---

**Need more help?** See the full guides in `/docs/` directory.
