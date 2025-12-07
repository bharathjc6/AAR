# RAG Pipeline Architecture

## Overview

The AAR (Automated Architecture Review) system uses a Retrieval-Augmented Generation (RAG) pipeline to efficiently analyze large codebases. Instead of sending entire files to LLMs, the system:

1. **Chunks** code into semantic units (classes, methods, namespaces)
2. **Embeds** chunks as vectors for similarity search
3. **Retrieves** relevant chunks based on agent queries
4. **Summarizes** context hierarchically when needed
5. **Synthesizes** findings with evidence linking

## Pipeline Flow

```
┌─────────────┐     ┌─────────────┐     ┌─────────────┐
│   Source    │────▶│  Chunker    │────▶│  Embedder   │
│   Files     │     │  (Roslyn)   │     │  (OpenAI)   │
└─────────────┘     └─────────────┘     └─────────────┘
                                               │
                                               ▼
┌─────────────┐     ┌─────────────┐     ┌─────────────┐
│   Agent     │◀────│ Retrieval   │◀────│   Vector    │
│  Response   │     │ Orchestrator│     │   Store     │
└─────────────┘     └─────────────┘     └─────────────┘
```

## Components

### 1. Tokenizer (`AAR.Shared.Tokenization`)

Accurate token counting for chunk sizing and budget management.

```csharp
public interface ITokenizer
{
    int CountTokens(string text);
    int[] Encode(string text);
    string Decode(int[] tokens);
    string TruncateToTokenLimit(string text, int maxTokens);
}
```

**Implementations:**
- `TiktokenTokenizer`: Uses OpenAI's tiktoken algorithm (accurate)
- `HeuristicTokenizer`: ~4 chars/token fallback (fast)

**Configuration:**
```json
{
  "Tokenizer": {
    "Type": "Tiktoken",
    "Model": "gpt-4o"
  }
}
```

### 2. Semantic Chunker (`AAR.Infrastructure.Services.Chunking`)

Splits source code into semantically meaningful chunks using Roslyn for C#.

```csharp
public interface IChunker
{
    Task<IReadOnlyList<ChunkInfo>> ChunkFilesAsync(
        Guid projectId,
        IReadOnlyDictionary<string, string> files,
        CancellationToken cancellationToken = default);
}
```

**Features:**
- **C# Semantic Splitting**: Uses Roslyn to split at namespace/class/method boundaries
- **Sliding Window Fallback**: For non-C# files, uses overlapping token windows
- **Deterministic IDs**: SHA256 hash of file path + content for change detection
- **Overlap**: Configurable token overlap to preserve context

**Configuration:**
```json
{
  "Chunker": {
    "MaxTokens": 1600,
    "OverlapTokens": 200,
    "MinTokens": 100
  }
}
```

### 3. Embedding Service (`AAR.Infrastructure.Services.Embedding`)

Generates vector embeddings for semantic similarity search.

```csharp
public interface IEmbeddingService
{
    Task<float[]> CreateEmbeddingAsync(string text, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<float[]>> BatchCreateEmbeddingsAsync(
        IReadOnlyList<string> texts, 
        CancellationToken cancellationToken = default);
}
```

**Implementations:**
- `AzureOpenAiEmbeddingService`: Production (text-embedding-3-small)
- `MockEmbeddingService`: Testing (deterministic pseudo-vectors)

**Configuration:**
```json
{
  "Embedding": {
    "Provider": "AzureOpenAI",
    "DeploymentName": "text-embedding-3-small",
    "Dimensions": 1536,
    "BatchSize": 100,
    "UseMock": false
  }
}
```

### 4. Vector Store (`AAR.Infrastructure.Services.VectorStore`)

Indexes and retrieves chunks by embedding similarity.

```csharp
public interface IVectorStore
{
    Task IndexAsync(IEnumerable<IndexedChunk> chunks, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<RetrievedChunk>> QueryAsync(
        float[] queryEmbedding, 
        int topK = 10, 
        float minSimilarity = 0.7f,
        CancellationToken cancellationToken = default);
}
```

**Implementations:**
- `InMemoryVectorStore`: Development/testing (cosine similarity)
- `CosmosVectorStore`: Production (planned)

**Configuration:**
```json
{
  "VectorStore": {
    "Provider": "InMemory"
  }
}
```

### 5. Retrieval Orchestrator (`AAR.Infrastructure.Services.Retrieval`)

Coordinates the full retrieval → summarization → synthesis flow.

```csharp
public interface IRetrievalOrchestrator
{
    Task<RetrievalResult> RetrieveContextAsync(
        Guid projectId,
        string query,
        int maxContextTokens = 8000,
        CancellationToken cancellationToken = default);
}
```

**Features:**
- **Query Embedding**: Converts agent queries to vectors
- **Top-K Retrieval**: Fetches most relevant chunks
- **Hierarchical Summarization**: Summarizes in buckets when context exceeds budget
- **Model Routing**: Uses smaller model for summaries, larger for synthesis
- **Token Budget**: Respects configurable context window limits

**Configuration:**
```json
{
  "Retrieval": {
    "MaxRetrievedChunks": 50,
    "TopK": 20,
    "SimilarityThreshold": 0.7,
    "MaxContextTokens": 12000,
    "EnableHierarchicalSummarization": true,
    "SummaryBucketSize": 5
  },
  "ModelRouter": {
    "SmallModel": "gpt-4o-mini",
    "LargeModel": "gpt-4o",
    "SmallModelThreshold": 4000
  }
}
```

### 6. Schema Validation (`AAR.Infrastructure.Services.Validation`)

Validates agent output against JSON Schema.

```csharp
public interface ISchemaValidator
{
    ValidationResult Validate(string json, string schemaName);
    bool TryFix(string json, string schemaName, out string fixedJson);
}
```

**Schemas:**
- `finding.schema.json`: Validates ReviewFinding structure

### 7. Telemetry (`AAR.Infrastructure.Services.Telemetry`)

Tracks pipeline metrics for monitoring and cost estimation.

```csharp
public interface IAnalysisTelemetry
{
    void RecordTokensConsumed(Guid projectId, int input, int output, string model);
    void RecordEmbeddingCall(Guid projectId, int textCount, int tokens, long durationMs);
    void RecordRetrieval(Guid projectId, int chunksRetrieved, long durationMs, bool summarized);
    CostEstimate EstimateCost(int inputTokens, int outputTokens, string modelName);
    TelemetrySummary GetProjectSummary(Guid projectId);
}
```

## Data Models

### Chunk Entity

```csharp
public class Chunk : BaseEntity
{
    public string ChunkHash { get; }      // Deterministic ID
    public Guid ProjectId { get; }
    public string FilePath { get; }
    public int StartLine { get; }
    public int EndLine { get; }
    public int TokenCount { get; }
    public string Language { get; }
    public string? SemanticType { get; }  // "class", "method", etc.
    public string? SemanticName { get; }  // Class/method name
    public string? Content { get; }
    public string? EmbeddingJson { get; } // Serialized float[]
}
```

### ChunkInfo (Processing)

```csharp
public record ChunkInfo
{
    public string ChunkId { get; init; }
    public string FilePath { get; init; }
    public int StartLine { get; init; }
    public int EndLine { get; init; }
    public int TokenCount { get; init; }
    public string? SemanticType { get; init; }
    public string? SemanticName { get; init; }
    public string Content { get; init; }
}
```

### IndexedChunk (Vector Store)

```csharp
public record IndexedChunk
{
    public string ChunkId { get; init; }
    public Guid ProjectId { get; init; }
    public string FilePath { get; init; }
    public int StartLine { get; init; }
    public int EndLine { get; init; }
    public float[] Embedding { get; init; }
    public string? Content { get; init; }
}
```

## Worker Integration

The `StartAnalysisConsumer` orchestrates the full pipeline:

```csharp
// 1. Load source files
var sourceFiles = LoadSourceFiles(workingDirectory);

// 2. Chunk all files
var allChunks = await _chunker.ChunkFilesAsync(project.Id, fileContents);

// 3. Embed chunks (batch)
var texts = allChunks.Select(c => c.Content).ToList();
var embeddings = await _embeddingService.BatchCreateEmbeddingsAsync(texts);

// 4. Index in vector store
var indexedChunks = allChunks.Zip(embeddings).Select((c, e) => new IndexedChunk { ... });
await _vectorStore.IndexAsync(indexedChunks);

// 5. Agents query the retrieval orchestrator
var context = await _retrievalOrchestrator.RetrieveContextAsync(
    projectId, 
    "Find security vulnerabilities in authentication code",
    maxContextTokens: 8000);
```

## Performance Considerations

### Token Budget

| Model | Context Window | Recommended Max |
|-------|----------------|-----------------|
| gpt-4o | 128K tokens | 12K for context |
| gpt-4o-mini | 128K tokens | 8K for context |

### Chunk Sizing

- **Max chunk**: 1600 tokens (fits multiple in context)
- **Overlap**: 200 tokens (preserves cross-boundary context)
- **Min chunk**: 100 tokens (avoids tiny fragments)

### Batch Processing

- Embeddings are batched (default: 100 per API call)
- Parallel processing where possible

## Extending the Pipeline

### Adding a New Vector Store

1. Implement `IVectorStore`
2. Register in `DependencyInjection.cs`
3. Add configuration options

### Adding Language-Specific Chunkers

The `SemanticChunker` can be extended to support more languages:

```csharp
private IReadOnlyList<ChunkInfo> ChunkForLanguage(string language, string content)
{
    return language switch
    {
        "csharp" => ChunkCSharp(content),
        "python" => ChunkPython(content),  // Future: AST-based
        "typescript" => ChunkTypeScript(content),  // Future
        _ => ChunkWithSlidingWindow(content)
    };
}
```

## Testing

### Mock Services

Use `MockEmbeddingService` for deterministic testing:

```csharp
services.AddSingleton<IEmbeddingService, MockEmbeddingService>();
```

### Integration Tests

```csharp
[Fact]
public async Task FullPipeline_ChunksAndRetrievesRelevantCode()
{
    // Arrange
    var files = new Dictionary<string, string>
    {
        ["src/Auth.cs"] = "public class AuthService { ... }"
    };
    
    // Act
    var chunks = await _chunker.ChunkFilesAsync(projectId, files);
    var embeddings = await _embeddingService.BatchCreateEmbeddingsAsync(chunks.Select(c => c.Content).ToList());
    await _vectorStore.IndexAsync(chunks.Zip(embeddings).Select(...));
    
    var result = await _orchestrator.RetrieveContextAsync(
        projectId, 
        "authentication security");
    
    // Assert
    result.Chunks.Should().Contain(c => c.FilePath == "src/Auth.cs");
}
```

## Monitoring

### Key Metrics

- **Chunks per project**: Indicator of codebase complexity
- **Embedding tokens**: Cost tracking
- **Retrieval latency**: Performance monitoring
- **Summarization ratio**: How often context exceeds budget

### Telemetry Summary

```csharp
var summary = _telemetry.GetProjectSummary(projectId);
// summary.TotalInputTokens, summary.TotalOutputTokens, 
// summary.EmbeddingCalls, summary.SummarizationCount
```

## Future Enhancements

1. **Cosmos DB Vector Store**: Production-grade vector storage
2. **Embedding Cache**: Redis-backed cache for repeated queries
3. **Incremental Indexing**: Only re-index changed files
4. **Multi-modal**: Support for diagrams and documentation
5. **Query Expansion**: Improve retrieval with query reformulation
